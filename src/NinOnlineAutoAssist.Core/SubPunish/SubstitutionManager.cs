using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Events;
using NinOnlineAutoAssist.Core.Logging;
using NinOnlineAutoAssist.Core.Models;

namespace NinOnlineAutoAssist.Core.SubPunish;

public sealed record SubstitutionOptions
{
    /// <summary>Configured key bound to the substitution technique. Game-specific; never hardcoded in logic.</summary>
    public string SubstitutionKey { get; init; } = "Q";
    /// <summary>Documented duration of base Substitution (~7s). EXPERIMENTAL — configurable, game changes.</summary>
    public double SubstitutionDurationSec { get; init; } = 7.0;
    /// <summary>Documented cooldown level 1 (~16s). EXPERIMENTAL — configurable per level/test.</summary>
    public double SubstitutionCooldownSec { get; init; } = 16.0;
    /// <summary>Resource cost of the activation (game-specific).</summary>
    public int ResourceCost { get; init; } = 0;
    /// <summary>Max time (ms) to wait for buff confirmation after requesting activation.</summary>
    public long ActivationConfirmTimeoutMs { get; init; } = 1200;
}

public sealed record SubstitutionReadiness(bool Ready, string Reason);

/// <summary>
/// Owns substitution availability: cooldown, resources, action blocks.
/// Requests activation through <see cref="IActionExecutor"/> and tracks confirmation.
/// Never assumes the buff is up — waits for an explicit BuffConfirmed event or timeout.
/// </summary>
public sealed class SubstitutionManager
{
    private readonly IActionExecutor _executor;
    private readonly IMonotonicClock _clock;
    private readonly IAutoAssistLogger _log;
    private SubstitutionOptions _options;

    private long _cooldownUntilMs;
    private long? _activationRequestedAtMs;

    public SubstitutionManager(IActionExecutor executor, IMonotonicClock clock,
        IAutoAssistLogger? logger = null, SubstitutionOptions? options = null)
    {
        _executor = executor;
        _clock = clock;
        _log = logger ?? NullLogger.Instance;
        _options = options ?? new SubstitutionOptions();
    }

    public SubstitutionOptions Options => _options;
    public void UpdateOptions(SubstitutionOptions options) => _options = options;

    public long? CooldownRemainingMs => Math.Max(0, _cooldownUntilMs - _clock.NowMs) is var r && r > 0 ? r : 0;

    public SubstitutionReadiness CheckReadiness(GameSnapshot snapshot)
    {
        var now = _clock.NowMs;

        // Provider-reported cooldown takes precedence when available (observed truth).
        if (snapshot.SubstitutionCooldownRemainingMs is { } providerCd && providerCd > 0)
            return new(false, $"Substitution on cooldown (provider: {providerCd} ms)");
        if (_cooldownUntilMs > now)
            return new(false, $"Substitution on cooldown ({_cooldownUntilMs - now} ms)");

        if (snapshot.Player.IsActionBlocked)
            return new(false, "Action blocked (casting/animation/stun)");
        if (snapshot.Player.IsCasting)
            return new(false, "Player is casting");
        if (snapshot.Player.Resources is { } res && res < _options.ResourceCost)
            return new(false, $"Insufficient resources ({res} < {_options.ResourceCost})");
        if (!snapshot.Player.IsAlive)
            return new(false, "Player is dead");

        return new(true, "Ready");
    }

    /// <summary>Request activation. Returns the outcome of the *request* (not of the buff).</summary>
    public ActionResult RequestActivation()
    {
        var result = _executor.Execute("substitution", _options.SubstitutionKey);
        if (result.Accepted)
        {
            _activationRequestedAtMs = _clock.NowMs;
            _log.Log(LogLevel.Info, "Substitution activation requested", EventOrigin.Requested);
        }
        else
        {
            _log.Log(LogLevel.Warning, $"Substitution activation rejected: {result.Reason}", EventOrigin.Requested);
        }
        return result;
    }

    /// <summary>Call when a BuffConfirmed event arrives (from any provider). Starts local cooldown tracking.</summary>
    public void NotifyBuffConfirmed()
    {
        _cooldownUntilMs = _clock.NowMs + (long)(_options.SubstitutionCooldownSec * 1000);
        _activationRequestedAtMs = null;
        _log.Log(LogLevel.Info, "Substitution buff confirmed", EventOrigin.Confirmed);
    }

    public long? ActivationRequestedAtMs => _activationRequestedAtMs;

    /// <summary>True when a confirmation is outstanding and its timeout expired.</summary>
    public bool IsConfirmationOverdue() =>
        _activationRequestedAtMs is { } t && _clock.NowMs - t > _options.ActivationConfirmTimeoutMs;

    /// <summary>Test/simulator hook: arm the local cooldown as if the sub had been used.</summary>
    public void ArmCooldownForSimulation() => NotifyBuffConfirmed();
}
