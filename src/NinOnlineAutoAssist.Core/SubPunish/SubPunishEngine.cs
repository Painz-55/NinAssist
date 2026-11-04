using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Events;
using NinOnlineAutoAssist.Core.Geometry;
using NinOnlineAutoAssist.Core.Logging;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Core.Metrics;

namespace NinOnlineAutoAssist.Core.SubPunish;

/// <summary>
/// Orchestrates the full Substitution -> teleport -> reorient -> counter-attack flow
/// as an explicit state machine. All transitions are logged with their evidence.
///
/// Reality checks enforced here:
///  - Detection is never treated as a hit; the attack must land or the sub must trigger.
///  - No buff confirmed (by timeout) => no counter-attack.
///  - Insufficient teleport evidence => cancelled, never assumed.
///  - Attacker identity mismatch => counter-attack cancelled (default policy).
///  - Requested/Executed actions are never presented as confirmed hits.
/// </summary>
public sealed class SubPunishEngine
{
    private readonly IOrientationController _orientation;
    private readonly IMonotonicClock _clock;
    private readonly IAutoAssistLogger _log;
    private readonly AttackDetectionEngine _detection;
    private readonly SubstitutionManager _substitution;
    private readonly SubTriggerVerifier _verifier;
    private readonly CounterAttackEngine _counter;
    private readonly MetricsCollector? _metrics;
    private SubPunishOptions _options;

    private GameSnapshot? _lastSnapshot;
    private (double X, double Y)? _lastObservedPosition;
    private (double X, double Y)? _windowAnchorHint;
    private long _threatDetectedAtMs = -1;
    private long _repositionConfirmedAtMs = -1;
    private string? _expectedAttackerId;
    private string? _confirmedAttackerId;
    private long? _lastActionRequestedAtMs;
    private bool _manualSelectedTarget;

    public SubPunishState State { get; private set; } = SubPunishState.Idle;
    public string? CancellationReason { get; private set; }
    public string? LastAttackerId => _manualSelectedTarget ? null : _confirmedAttackerId ?? _expectedAttackerId;

    public SubPunishEngine(IOrientationController orientation, IMonotonicClock clock,
        AttackDetectionEngine detection, SubstitutionManager substitution,
        SubTriggerVerifier verifier, CounterAttackEngine counter,
        IAutoAssistLogger? logger = null, SubPunishOptions? options = null,
        MetricsCollector? metrics = null)
    {
        _orientation = orientation;
        _clock = clock;
        _log = logger ?? NullLogger.Instance;
        _detection = detection;
        _substitution = substitution;
        _verifier = verifier;
        _counter = counter;
        _options = options ?? new SubPunishOptions();
        _metrics = metrics;
    }

    public SubPunishOptions Options => _options;
    public void UpdateOptions(SubPunishOptions options) => _options = options;

    public void ResetToIdle()
    {
        State = SubPunishState.Idle;
        CancellationReason = null;
        _threatDetectedAtMs = -1;
        _repositionConfirmedAtMs = -1;
        _expectedAttackerId = null;
        _confirmedAttackerId = null;
        _manualSelectedTarget = false;
        _verifier.Reset();
        _counter.Reset();
    }

    // ───────────────────────────────────────────────────────────── entry points

    public void HandleEvent(CombatEvent evt, GameSnapshot? snapshot = null)
    {
        if (evt.Kind is CombatEventKind.ManualInterrupt)
        {
            Cancel("Manual interrupt");
            return;
        }

        // Track the last observed position BEFORE this event is processed; it becomes the
        // teleport anchor for any evidence window opened by this event.
        _windowAnchorHint = _lastObservedPosition;
        if (snapshot is not null) _lastSnapshot = snapshot;

        // Terminal states auto-reset on a new threat.
        if (State is SubPunishState.Completed or SubPunishState.Cancelled)
        {
            if (evt.Kind == CombatEventKind.ThreatDetected && _options.Enabled) ResetToIdle();
            else if (evt.Kind == CombatEventKind.BuffConfirmed && _options.Enabled && _options.AdoptManualSubstitutionForSelectedTarget) ResetToIdle();
            else return;
        }

        switch (evt.Kind)
        {
            case CombatEventKind.ThreatDetected: OnThreatDetected(evt); break;
            case CombatEventKind.AttackLanded: OnAttackLanded(evt); break;
            case CombatEventKind.BuffConfirmed: OnBuffConfirmed(evt); break;
            case CombatEventKind.BuffExpired: OnBuffExpired(evt); break;
            case CombatEventKind.SubstitutionTriggered: OnSubTriggered(evt); break;
            case CombatEventKind.TeleportObserved: OnTeleportObserved(evt); break;
            case CombatEventKind.CombatStateChanged: OnCombatStateChanged(evt); break;
            case CombatEventKind.ActionExecuted:
                _log.Log(LogLevel.Info, $"Action executed: {evt.Note ?? evt.AttackType ?? "?"}", evt.Origin);
                break;
        }

        if (_lastSnapshot is not null)
            _lastObservedPosition = (_lastSnapshot.Player.PositionX, _lastSnapshot.Player.PositionY);
        _windowAnchorHint = null;
    }

    public void Tick(GameSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        if (!_options.Enabled)
        {
            if (State != SubPunishState.Idle) Cancel("SubPunish disabled");
            return;
        }

        var now = _clock.NowMs;

        if (_manualSelectedTarget && (snapshot.Target is not { IsAlive: true, IsTargeted: true } ||
            snapshot.Target.Id != _expectedAttackerId))
        {
            Cancel("Selected counter target changed/disappeared; attacker identity is not inferred");
            return;
        }

        // Keep position evidence fresh for an open verifier window.
        if (_verifier.HasOpenWindow)
        {
            _verifier.ApplyPositionObservation(snapshot.Player.PositionX, snapshot.Player.PositionY, EventOrigin.Observed);
            EvaluateVerification();
        }

        switch (State)
        {
            case SubPunishState.WaitingForAttack:
                if (_threatDetectedAtMs >= 0 && now - _threatDetectedAtMs > _options.ThreatExpiryMs)
                    Cancel("Threat expired before attack landed");
                break;

            case SubPunishState.SubActivationRequested:
                if (_substitution.IsConfirmationOverdue())
                    Cancel("Buff confirmation timed out");
                break;

            case SubPunishState.SubActive:
                // Buff lifetime expiry is handled via BuffExpired event from provider/simulator.
                break;

            case SubPunishState.SubTriggered:
                EvaluateVerification();
                break;

            case SubPunishState.TurningToAttacker:
                TickReorientation(snapshot);
                break;

            case SubPunishState.CounterattackReady:
                TryBeginCounterAttack(snapshot);
                break;

            case SubPunishState.CounterattackExecuting:
                TickCounterAttack(snapshot);
                break;
        }

        _lastObservedPosition = (snapshot.Player.PositionX, snapshot.Player.PositionY);
    }

    // ───────────────────────────────────────────────────────────── handlers

    private void OnThreatDetected(CombatEvent evt)
    {
        if (!_options.Enabled || !_options.AutoPrepareSub)
        {
            _log.Log(LogLevel.Debug, "Threat detected but SubPunish/AutoPrepareSub disabled", evt.Origin);
            return;
        }
        if (_lastSnapshot is null)
        {
            _log.Log(LogLevel.Warning, "Threat ignored: no snapshot available", evt.Origin);
            return;
        }

        var assessment = _detection.Assess(evt, _lastSnapshot);
        if (!assessment.IsValidThreat)
        {
            _log.Log(LogLevel.Info, $"Threat rejected: {assessment.Reason}", evt.Origin);
            return;
        }

        _expectedAttackerId = evt.AttackerId;
        _threatDetectedAtMs = _clock.NowMs;
        _metrics?.RecordThreat();

        var readiness = _substitution.CheckReadiness(_lastSnapshot);
        if (!readiness.Ready)
        {
            _log.Log(LogLevel.Warning, $"Cannot prepare substitution: {readiness.Reason}", EventOrigin.Observed);
            State = SubPunishState.WaitingForAttack; // Attack may still land — we just can't shield.
            return;
        }

        var result = _substitution.RequestActivation();
        State = result.Accepted
            ? SubPunishState.SubActivationRequested
            : SubPunishState.WaitingForAttack;
        if (result.Accepted) _metrics?.RecordActivationRequested();
    }

    private void OnAttackLanded(CombatEvent evt)
    {
        if (!TargetsLocalPlayer(evt)) return;

        switch (State)
        {
            case SubPunishState.WaitingForAttack:
                Cancel("Attack landed before substitution could be prepared/confirmed");
                break;

            case SubPunishState.SubActivationRequested:
                _metrics?.RecordAttackBeforeActivation();
                Cancel("Attack landed before buff confirmation");
                break;

            case SubPunishState.SubActive:
                OpenEvidenceWindow(evt);
                // AttackLanded is NOT explicit trigger evidence by itself — it only opens the window.
                _verifier.ApplyAttackerIdentified(evt.AttackerId ?? _expectedAttackerId ?? "", evt.Origin);
                break;

            default:
                // Unrelated hit outside the flow; ignored for sequencing.
                break;
        }
    }

    private void OnBuffConfirmed(CombatEvent evt)
    {
        if (State == SubPunishState.Idle && _options.Enabled && _options.AdoptManualSubstitutionForSelectedTarget &&
            _lastSnapshot is { Player.IsAlive: true, Target: { IsAlive: true, IsTargeted: true } target } &&
            _lastSnapshot.ActiveBuffs.Contains("Substitution Technique"))
        {
            _expectedAttackerId = target.Id; // Explicit counter target, NOT an identified attacker.
            _manualSelectedTarget = true;
            _substitution.NotifyBuffConfirmed();
            _metrics?.RecordBuffConfirmed();
            State = SubPunishState.SubActive;
            _log.Log(LogLevel.Info, "Manual Substitution observed; selected counter target locked (attacker identity unavailable)", evt.Origin);
            return;
        }
        if (State is not (SubPunishState.SubActivationRequested or SubPunishState.SubActive)) return;
        _substitution.NotifyBuffConfirmed();
        _metrics?.RecordBuffConfirmed();
        State = SubPunishState.SubActive;
        _log.Log(LogLevel.Info, "Substitution active — waiting for attack/trigger", evt.Origin);
    }

    private void OnBuffExpired(CombatEvent evt)
    {
        if (State == SubPunishState.SubActive)
        {
            if (_verifier.HasOpenWindow)
            {
                _verifier.ApplyBuffDisappeared(evt.Origin);
                EvaluateVerification();
                return;
            }
            // Natural expiry without any attack: sub consumed nothing, no teleport.
            _log.Log(LogLevel.Info, "Substitution expired without being triggered", evt.Origin);
            ResetToIdle();
        }
        else if (State == SubPunishState.SubTriggered)
        {
            _verifier.ApplyBuffDisappeared(evt.Origin);
            EvaluateVerification();
        }
    }

    private void OnSubTriggered(CombatEvent evt)
    {
        if (State is not (SubPunishState.SubActive or SubPunishState.SubTriggered)) return;
        if (!AdoptAttacker(evt.AttackerId, evt.Origin)) return; // cancelled by policy
        OpenEvidenceWindow(evt);
        _verifier.ApplyExplicitTrigger(evt.Origin);
        _verifier.ApplyAttackerIdentified(evt.AttackerId ?? _expectedAttackerId ?? "", evt.Origin);
        // NOTE: no CombatStateChanged credit here — that evidence must come from an explicit
        // CombatStateChanged event, otherwise a bare trigger would self-corroborate.
        State = SubPunishState.SubTriggered;
        EvaluateVerification();
    }

    private void OnTeleportObserved(CombatEvent evt)
    {
        if (State is not (SubPunishState.SubActive or SubPunishState.SubTriggered)) return;
        if (!AdoptAttacker(evt.AttackerId, evt.Origin)) return; // cancelled by policy
        OpenEvidenceWindow(evt);
        if (evt is { PositionX: not null, PositionY: not null })
            _verifier.ApplyPositionObservation(evt.PositionX.Value, evt.PositionY.Value, evt.Origin);
        State = SubPunishState.SubTriggered;
        EvaluateVerification();
    }

    private void OnCombatStateChanged(CombatEvent evt)
    {
        if (State == SubPunishState.SubTriggered && _verifier.HasOpenWindow)
        {
            _verifier.ApplyCombatStateChanged(evt.Origin);
            EvaluateVerification();
        }
    }

    // ───────────────────────────────────────────────────────────── reposition / counter

    private void EvaluateVerification()
    {
        var result = _verifier.Evaluate();
        switch (result)
        {
            case SubVerificationResult.Confirmed:
                _verifier.Reset();
                _repositionConfirmedAtMs = _clock.NowMs;
                State = SubPunishState.RepositioningConfirmed;
                _metrics?.RecordSubConfirmed();
                _log.Log(LogLevel.Info, "Repositioning confirmed after substitution", EventOrigin.Confirmed);
                if (_lastSnapshot is not null) TickReorientation(_lastSnapshot);
                break;
            case SubVerificationResult.TimedOut:
                // Evidence insufficient: never assume a teleport happened.
                Cancel("Teleport not confirmed within timeout");
                break;
        }
    }

    private void TickReorientation(GameSnapshot snapshot)
    {
        if (State is not (SubPunishState.RepositioningConfirmed or SubPunishState.TurningToAttacker)) return;

        var attacker = FindAttacker(snapshot, _confirmedAttackerId ?? _expectedAttackerId);
        if (attacker is null)
        {
            Cancel("Attacker position unavailable — cannot orient");
            return;
        }

        double dx = attacker.PositionX - snapshot.Player.PositionX;
        double dy = attacker.PositionY - snapshot.Player.PositionY;

        if (DirectionCalculator.IsFacingTowards(snapshot.Player.Facing, dx, dy))
        {
            State = SubPunishState.CounterattackReady;
            _metrics?.RecordAlignmentConfirmed();
            _log.Log(LogLevel.Info, _manualSelectedTarget ? "Facing selected counter target confirmed (attacker unknown)" : "Facing attacker confirmed", EventOrigin.Confirmed);
            return;
        }

        // Reorientation uses zero positional tolerance: the correction must be exact enough to attack.
        var desired = DirectionCalculator.Calculate(dx, dy, snapshot.Player.Facing,
            new DirectionCalculatorOptions { PositionTolerance = 0.0, HysteresisRatio = 1.0 });
        if (desired == Direction.None)
        {
            // On top of the attacker (within tolerance): any facing connects; don't turn.
            State = SubPunishState.CounterattackReady;
            return;
        }

        // Orientation timeout guard.
        if (_repositionConfirmedAtMs >= 0 &&
            _clock.NowMs - _repositionConfirmedAtMs > _options.OrientationTimeoutMs)
        {
            Cancel("Orientation timed out");
            return;
        }

        var result = _orientation.RequestTurn(desired);
        State = SubPunishState.TurningToAttacker;
        if (!result.Accepted)
            _log.Log(LogLevel.Warning, $"Turn request rejected: {result.Reason}", EventOrigin.Requested);
    }

    private void TryBeginCounterAttack(GameSnapshot snapshot)
    {
        if (_options.OrientationOnly)
        {
            State = SubPunishState.Completed;
            _log.Log(LogLevel.Info, "Orientation-only flow completed; no offensive action requested", EventOrigin.Observed);
            return;
        }
        if (_repositionConfirmedAtMs < 0) { Cancel("No reposition confirmation timestamp"); return; }
        var attacker = FindAttacker(snapshot, _confirmedAttackerId ?? _expectedAttackerId);
        double dist = attacker is null
            ? double.MaxValue
            : DirectionCalculator.Distance(attacker.PositionX - snapshot.Player.PositionX,
                                           attacker.PositionY - snapshot.Player.PositionY);

        var check = _counter.CheckWindow(_repositionConfirmedAtMs, snapshot, dist);
        if (!check.Ok) { Cancel(check.Reason); return; }

        _counter.BeginSequence();
        _metrics?.RecordCounterAttackStarted();
        State = SubPunishState.CounterattackExecuting;
        _lastActionRequestedAtMs = null;
        TickCounterAttack(snapshot);
    }

    private void TickCounterAttack(GameSnapshot snapshot)
    {
        var now = _clock.NowMs;
        var attacker = FindAttacker(snapshot, _confirmedAttackerId ?? _expectedAttackerId);
        double dist = attacker is null
            ? double.MaxValue
            : DirectionCalculator.Distance(attacker.PositionX - snapshot.Player.PositionX,
                                           attacker.PositionY - snapshot.Player.PositionY);

        // Inter-action pacing: don't fire everything at once.
        if (_lastActionRequestedAtMs is { } last && now - last < _counter.Options.InterActionIntervalMs) return;
        if (now < _counter.NextActionEarliestAtMs) return;

        var result = _counter.Step(snapshot, dist);
        switch (result)
        {
            case CounterAttackStepResult.Executed:
                _lastActionRequestedAtMs = now;
                if (_metrics?.FirstActionRecorded != true) _metrics?.RecordFirstAction();
                break;
            case CounterAttackStepResult.Skipped:
                _lastActionRequestedAtMs = now;
                break;
            case CounterAttackStepResult.Completed:
                State = SubPunishState.Completed;
                _metrics?.RecordSequenceCompleted();
                _log.Log(LogLevel.Info, "Counter-attack sequence completed (hit not confirmed — no ActionConfirmed event)", EventOrigin.Executed);
                break;
            case CounterAttackStepResult.Aborted:
                Cancel($"Counter-attack sequence aborted: {_counter.LastAbortReason ?? "unknown"}");
                break;
        }
    }

    // ───────────────────────────────────────────────────────────── helpers

    private void OpenEvidenceWindow(CombatEvent evt)
    {
        if (_verifier.HasOpenWindow) return;
        var snap = _lastSnapshot;
        // Prefer the pre-event anchor (position before the teleport), fall back to current snapshot.
        double px = _windowAnchorHint?.X ?? snap?.Player.PositionX ?? evt.PositionX ?? 0;
        double py = _windowAnchorHint?.Y ?? snap?.Player.PositionY ?? evt.PositionY ?? 0;
        _verifier.OpenWindow(px, py, _expectedAttackerId);
        State = SubPunishState.SubTriggered;
    }

    private bool AdoptAttacker(string? attackerId, EventOrigin origin)
    {
        if (string.IsNullOrWhiteSpace(attackerId)) return true; // nothing to adopt; keep existing
        if (_expectedAttackerId is not null &&
            !string.Equals(attackerId, _expectedAttackerId, StringComparison.Ordinal))
        {
            if (_options.TargetSelectionPolicy == TargetSelectionPolicy.CancelOnMismatch)
            {
                Cancel($"Attacker mismatch (expected '{_expectedAttackerId}', got '{attackerId}') — policy CancelOnMismatch");
                return false;
            }
            _log.Log(LogLevel.Warning, $"Adopting different attacker '{attackerId}' (policy ProceedWithAttacker)", origin);
        }
        _confirmedAttackerId = attackerId;
        return true;
    }

    private static TargetInfo? FindAttacker(GameSnapshot snapshot, string? id)
    {
        if (id is null) return snapshot.Target;
        var match = snapshot.KnownEntities.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));
        if (match is not null) return match;
        return snapshot.Target is not null && string.Equals(snapshot.Target.Id, id, StringComparison.Ordinal)
            ? snapshot.Target
            : null;
    }

    private bool TargetsLocalPlayer(CombatEvent evt) =>
        evt.TargetId is null ||
        (_lastSnapshot is not null && string.Equals(evt.TargetId, _lastSnapshot.Player.Id, StringComparison.Ordinal));

    private void Cancel(string reason)
    {
        if (_manualSelectedTarget) { _expectedAttackerId = null; _confirmedAttackerId = null; }
        _manualSelectedTarget = false;
        State = SubPunishState.Cancelled;
        CancellationReason = reason;
        _verifier.Reset();
        _counter.Reset();
        _metrics?.RecordSequenceCancelled(reason);
        _log.Log(LogLevel.Warning, $"SubPunish cancelled: {reason}", EventOrigin.Observed);
    }
}
