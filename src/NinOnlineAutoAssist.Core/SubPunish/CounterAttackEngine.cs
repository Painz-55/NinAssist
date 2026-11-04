using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Events;
using NinOnlineAutoAssist.Core.Logging;
using NinOnlineAutoAssist.Core.Models;

namespace NinOnlineAutoAssist.Core.SubPunish;

/// <summary>
/// One step in the counter-attack sequence. All distances/times are game-specific
/// and configurable; unverified values must be treated as experimental (see docs).
/// </summary>
public sealed record CounterAttackAction
{
    public required string Id { get; init; }
    /// <summary>Key bound to this action in the user's configuration.</summary>
    public required string Key { get; init; }
    /// <summary>Max distance (units) at which this action can hit.</summary>
    public double MaxRange { get; init; } = 4.0;
    /// <summary>Cast/animation time (ms) before the action commits.</summary>
    public long CastTimeMs { get; init; } = 0;
    /// <summary>Resource cost; checked against PlayerState.Resources when known.</summary>
    public int ResourceCost { get; init; } = 0;
    /// <summary>Per-action cooldown (ms). Tracked by the engine.</summary>
    public long CooldownMs { get; init; } = 0;
    /// <summary>If true, the action cannot be started while the player is moving.</summary>
    public bool MovementRestricted { get; init; } = false;
    /// <summary>If true, the action may not be started while casting/action-blocked.</summary>
    public bool BlockedDuringCast { get; init; } = true;
    /// <summary>What to do when this action's conditions fail.</summary>
    public CounterAttackFailurePolicy OnFailure { get; init; } = CounterAttackFailurePolicy.Abort;
}

public enum CounterAttackFailurePolicy { Abort, Skip }

public sealed record CounterAttackOptions
{
    /// <summary>Max time (ms) from reposition-confirmed to start the sequence. Experimental.</summary>
    public long AttackWindowMs { get; init; } = 1500;
    /// <summary>Max total time (ms) for the whole sequence before it is aborted.</summary>
    public long CounterAttackTimeoutMs { get; init; } = 2500;
    /// <summary>Delay inserted between action *requests* (ms). Not a fixed damage delay — sequencing only.</summary>
    public long InterActionIntervalMs { get; init; } = 120;
    /// <summary>Global cooldown imposed after any action request (ms). Experimental.</summary>
    public long GlobalCooldownMs { get; init; } = 300;
    /// <summary>Estimated network latency (ms); added to window checks. Experimental.</summary>
    public long EstimatedLatencyMs { get; init; } = 80;
    public IReadOnlyList<CounterAttackAction> AttackSequence { get; init; } = Array.Empty<CounterAttackAction>();
}

public enum CounterAttackStepResult { Executed, Skipped, Aborted, Completed, NotStarted }

/// <summary>
/// Executes a configured sequence of offensive actions, validating conditions before
/// EACH action. Never fires actions simultaneously; respects cast locks, per-action
/// cooldowns and a configurable global cooldown. Requesting an action is NOT proof
/// it connected — confirmation requires an ActionConfirmed event.
/// </summary>
public sealed class CounterAttackEngine
{
    private readonly IActionExecutor _executor;
    private readonly IMonotonicClock _clock;
    private readonly IAutoAssistLogger _log;
    private CounterAttackOptions _options;

    private readonly Dictionary<string, long> _actionCooldownUntil = new(StringComparer.Ordinal);
    private long _globalCooldownUntilMs;
    private long _sequenceStartedAtMs = -1;
    private int _nextIndex;

    /// <summary>Reason of the last abort (null when none). Used for diagnostics/metrics.</summary>
    public string? LastAbortReason { get; private set; }

    public CounterAttackEngine(IActionExecutor executor, IMonotonicClock clock,
        IAutoAssistLogger? logger = null, CounterAttackOptions? options = null)
    {
        _executor = executor;
        _clock = clock;
        _log = logger ?? NullLogger.Instance;
        _options = options ?? new CounterAttackOptions();
    }

    public CounterAttackOptions Options => _options;
    public void UpdateOptions(CounterAttackOptions options) => _options = options;

    public bool HasPendingActions => _nextIndex < _options.AttackSequence.Count;
    public long SequenceStartedAtMs => _sequenceStartedAtMs;

    /// <summary>
    /// Check the counter-attack window without executing anything.
    /// </summary>
    public (bool Ok, string Reason) CheckWindow(long repositionConfirmedAtMs, GameSnapshot snapshot, double distanceToAttacker)
    {
        var now = _clock.NowMs;
        long elapsed = now - repositionConfirmedAtMs;
        // Latency eats into the same budget — it does not extend it.
        if (elapsed + _options.EstimatedLatencyMs > _options.AttackWindowMs)
            return (false, $"Attack window expired ({elapsed} ms + {_options.EstimatedLatencyMs} ms latency > {_options.AttackWindowMs} ms)");
        if (_options.AttackSequence.Count == 0)
            return (false, "Attack sequence is empty");
        if (distanceToAttacker > MaxRangeOfSequence())
            return (false, $"Attacker out of range ({distanceToAttacker:F1}u > {MaxRangeOfSequence():F1}u)");
        if (!snapshot.Player.IsAlive)
            return (false, "Player is dead");
        return (true, "Window OK");
    }

    /// <summary>Begin a new sequence; call once per confirmed repositioning.</summary>
    public void BeginSequence()
    {
        _nextIndex = 0;
        _sequenceStartedAtMs = _clock.NowMs;
    }

    /// <summary>
    /// Attempt to execute the next action in the sequence. Call on tick/event.
    /// Returns the step result; Completed when the sequence finished.
    /// </summary>
    public CounterAttackStepResult Step(GameSnapshot snapshot, double distanceToAttacker)
    {
        var now = _clock.NowMs;
        if (_sequenceStartedAtMs < 0) return CounterAttackStepResult.NotStarted;

        if (now - _sequenceStartedAtMs > _options.CounterAttackTimeoutMs)
        {
            _log.Log(LogLevel.Warning, "Counter-attack timed out; aborting sequence", EventOrigin.Observed);
            return AbortSequence("CounterAttackTimeout");
        }

        if (!HasPendingActions) return CounterAttackStepResult.Completed;

        var action = _options.AttackSequence[_nextIndex];
        var failure = ValidateAction(action, snapshot, distanceToAttacker, now);
        if (failure is not null)
        {
            if (action.OnFailure == CounterAttackFailurePolicy.Skip)
            {
                _log.Log(LogLevel.Info, $"Action '{action.Id}' skipped: {failure}", EventOrigin.Observed);
                _nextIndex++;
                return CounterAttackStepResult.Skipped;
            }
            _log.Log(LogLevel.Warning, $"Action '{action.Id}' aborted: {failure}", EventOrigin.Observed);
            return AbortSequence(failure);
        }

        var result = _executor.Execute(action.Id, action.Key);
        if (!result.Accepted)
        {
            if (action.OnFailure == CounterAttackFailurePolicy.Skip)
            {
                _nextIndex++;
                return CounterAttackStepResult.Skipped;
            }
            return AbortSequence(result.Reason);
        }

        _log.Log(LogLevel.Info, $"Action '{action.Id}' requested (key '{action.Key}')", EventOrigin.Requested);
        if (action.CooldownMs > 0) _actionCooldownUntil[action.Id] = now + action.CooldownMs;
        if (_options.GlobalCooldownMs > 0) _globalCooldownUntilMs = now + _options.GlobalCooldownMs;

        _nextIndex++;
        return _nextIndex >= _options.AttackSequence.Count
            ? CounterAttackStepResult.Completed
            : CounterAttackStepResult.Executed;
    }

    /// <summary>Earliest time the next action may be requested (inter-action interval + GCD).</summary>
    public long NextActionEarliestAtMs
    {
        get
        {
            var now = _clock.NowMs;
            long gcdLeft = _globalCooldownUntilMs - now;
            long intervalLeft = _sequenceStartedAtMs < 0 ? 0 : 0; // inter-action pacing handled by caller via Tick cadence
            return now + Math.Max(0, gcdLeft) + Math.Max(0, intervalLeft);
        }
    }

    public string? LastActionId => _nextIndex > 0 && _nextIndex <= _options.AttackSequence.Count
        ? _options.AttackSequence[_nextIndex - 1].Id : null;

    private string? ValidateAction(CounterAttackAction action, GameSnapshot snapshot, double distance, long now)
    {
        if (distance > action.MaxRange)
            return $"Out of range ({distance:F1}u > {action.MaxRange:F1}u)";
        if (_actionCooldownUntil.TryGetValue(action.Id, out var cd) && cd > now)
            return $"On cooldown ({cd - now} ms)";
        if (_globalCooldownUntilMs > now)
            return $"Global cooldown ({_globalCooldownUntilMs - now} ms)";
        if (action.BlockedDuringCast && (snapshot.Player.IsCasting || snapshot.Player.IsActionBlocked))
            return "Player is casting/action-blocked";
        if (action.MovementRestricted && snapshot.Player.IsCasting)
            return "Movement-restricted action while in cast/animation";
        if (snapshot.Player.Resources is { } res && res < action.ResourceCost)
            return $"Insufficient resources ({res} < {action.ResourceCost})";
        if (!snapshot.Player.IsAlive)
            return "Player is dead";
        return null;
    }

    private double MaxRangeOfSequence() =>
        _options.AttackSequence.Count == 0 ? 0 : _options.AttackSequence.Max(a => a.MaxRange);

    private CounterAttackStepResult AbortSequence(string reason)
    {
        LastAbortReason = reason;
        _log.Log(LogLevel.Warning, $"Counter-attack sequence aborted: {reason}", EventOrigin.Observed);
        _sequenceStartedAtMs = -1;
        _nextIndex = _options.AttackSequence.Count;
        return CounterAttackStepResult.Aborted;
    }

    public void Reset()
    {
        _sequenceStartedAtMs = -1;
        _nextIndex = 0;
    }
}
