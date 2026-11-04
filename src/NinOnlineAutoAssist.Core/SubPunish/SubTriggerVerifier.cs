using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Events;
using NinOnlineAutoAssist.Core.Logging;
using NinOnlineAutoAssist.Core.Models;

namespace NinOnlineAutoAssist.Core.SubPunish;

public sealed record SubTriggerVerificationOptions
{
    /// <summary>Max time (ms) to gather evidence after a raw trigger signal before giving up.</summary>
    public long TeleportConfirmationTimeoutMs { get; init; } = 1500;
    /// <summary>Minimum jump distance (units) to count as a teleport. Filters micro-movements. EXPERIMENTAL for your coordinate scale.</summary>
    public double TeleportMinDistance { get; init; } = 1.5;
    /// <summary>Maximum plausible teleport distance (units). Beyond this it's a map change, not a sub.</summary>
    public double TeleportMaxDistance { get; init; } = 30.0;
    /// <summary>Coherence window (ms) between the trigger signal and the position jump.</summary>
    public long EvidenceCoherenceWindowMs { get; init; } = 800;

    // Evidence weights (configurable).
    public int WeightBuffDisappeared { get; init; } = 2;
    public int WeightPositionJumpCoherent { get; init; } = 2;
    public int WeightExplicitTriggerEvent { get; init; } = 3;
    public int WeightAttackerIdentified { get; init; } = 1;
    public int WeightCombatStateChanged { get; init; } = 1;

    /// <summary>Minimum total score to confirm. Must include at least one strong signal.</summary>
    public int MinEvidenceScore { get; init; } = 4;
}

public enum SubVerificationResult { Pending, Confirmed, TimedOut }

/// <summary>
/// Collects heterogeneous evidence that a substitution actually triggered and decides
/// Confirmed/TimedOut. A mere position jump is NEVER sufficient on its own.
/// </summary>
public sealed class SubTriggerVerifier
{
    private readonly IMonotonicClock _clock;
    private readonly IAutoAssistLogger _log;
    private SubTriggerVerificationOptions _options;

    private long? _windowOpenedAtMs;
    private double _preSubX, _preSubY;
    private string? _expectedAttackerId;

    private int _score;
    private bool _sawBuffDisappear;
    private bool _sawPositionJump;
    private bool _sawExplicitTrigger;
    private bool _sawAttackerIdentified;
    private bool _sawCombatStateChanged;

    public SubTriggerVerifier(IMonotonicClock clock, IAutoAssistLogger? logger = null,
        SubTriggerVerificationOptions? options = null)
    {
        _clock = clock;
        _log = logger ?? NullLogger.Instance;
        _options = options ?? new SubTriggerVerificationOptions();
    }

    public SubTriggerVerificationOptions Options => _options;
    public void UpdateOptions(SubTriggerVerificationOptions options) => _options = options;

    public bool HasOpenWindow => _windowOpenedAtMs.HasValue;

    /// <summary>Open the evidence window. Call when SUB_ACTIVE and a trigger-ish signal arrives or sub is expected.</summary>
    public void OpenWindow(double preSubX, double preSubY, string? expectedAttackerId)
    {
        _windowOpenedAtMs = _clock.NowMs;
        _preSubX = preSubX; _preSubY = preSubY;
        _expectedAttackerId = expectedAttackerId;
        _score = 0;
        _sawBuffDisappear = _sawPositionJump = _sawExplicitTrigger = _sawAttackerIdentified = _sawCombatStateChanged = false;
    }

    public void Reset()
    {
        _windowOpenedAtMs = null;
        _score = 0;
        _sawBuffDisappear = _sawPositionJump = _sawExplicitTrigger = _sawAttackerIdentified = _sawCombatStateChanged = false;
    }

    /// <summary>Feed a buff-expired/disappeared observation. Note: expiry WITHOUT trigger is also possible — see ApplyBuffExpired.</summary>
    public void ApplyBuffDisappeared(EventOrigin origin)
    {
        if (_windowOpenedAtMs is null || _sawBuffDisappear) return;
        _sawBuffDisappear = true;
        _score += _options.WeightBuffDisappeared;
        _log.Log(LogLevel.Debug, $"Evidence: buff disappeared (+{_options.WeightBuffDisappeared}) score={_score}", origin);
    }

    /// <summary>Feed a position observation; classifies jumps within/ beyond the plausible teleport band.</summary>
    public void ApplyPositionObservation(double x, double y, EventOrigin origin)
    {
        if (_windowOpenedAtMs is null || _sawPositionJump) return;
        double dist = Geometry.DirectionCalculator.Distance(x - _preSubX, y - _preSubY);
        if (dist >= _options.TeleportMinDistance && dist <= _options.TeleportMaxDistance)
        {
            if (_clock.NowMs - _windowOpenedAtMs <= _options.EvidenceCoherenceWindowMs)
            {
                _sawPositionJump = true;
                _score += _options.WeightPositionJumpCoherent;
                _log.Log(LogLevel.Debug, $"Evidence: coherent position jump {dist:F1}u (+{_options.WeightPositionJumpCoherent}) score={_score}", origin);
            }
        }
    }

    public void ApplyExplicitTrigger(EventOrigin origin)
    {
        if (_windowOpenedAtMs is null || _sawExplicitTrigger) return;
        _sawExplicitTrigger = true;
        _score += _options.WeightExplicitTriggerEvent;
        _log.Log(LogLevel.Debug, $"Evidence: explicit trigger event (+{_options.WeightExplicitTriggerEvent}) score={_score}", origin);
    }

    public void ApplyAttackerIdentified(string attackerId, EventOrigin origin)
    {
        if (_windowOpenedAtMs is null || _sawAttackerIdentified) return;
        if (_expectedAttackerId is not null &&
            !string.Equals(attackerId, _expectedAttackerId, StringComparison.Ordinal))
        {
            _log.Log(LogLevel.Warning, $"Evidence rejected: attacker '{attackerId}' != expected '{_expectedAttackerId}'", origin);
            return;
        }
        _sawAttackerIdentified = true;
        _score += _options.WeightAttackerIdentified;
        _log.Log(LogLevel.Debug, $"Evidence: attacker identified (+{_options.WeightAttackerIdentified}) score={_score}", origin);
    }

    public void ApplyCombatStateChanged(EventOrigin origin)
    {
        if (_windowOpenedAtMs is null || _sawCombatStateChanged) return;
        _sawCombatStateChanged = true;
        _score += _options.WeightCombatStateChanged;
        _log.Log(LogLevel.Debug, $"Evidence: combat state changed (+{_options.WeightCombatStateChanged}) score={_score}", origin);
    }

    /// <summary>Evaluate current evidence. Confirmed requires min score AND at least one strong signal.</summary>
    public SubVerificationResult Evaluate()
    {
        if (_windowOpenedAtMs is null) return SubVerificationResult.Pending;

        bool strongSignal = _sawExplicitTrigger || (_sawBuffDisappear && _sawPositionJump);
        bool corroboration = _sawPositionJump || _sawBuffDisappear || _sawCombatStateChanged;
        if (_score >= _options.MinEvidenceScore && strongSignal && corroboration)
        {
            _log.Log(LogLevel.Info, $"Substitution confirmed (score={_score})", EventOrigin.Confirmed);
            return SubVerificationResult.Confirmed;
        }

        if (_clock.NowMs - _windowOpenedAtMs > _options.TeleportConfirmationTimeoutMs)
        {
            _log.Log(LogLevel.Warning, $"Teleport confirmation timed out (score={_score}, strong={strongSignal})", EventOrigin.Observed);
            return SubVerificationResult.TimedOut;
        }

        return SubVerificationResult.Pending;
    }
}
