using NinOnlineAutoAssist.Core.Models;

namespace NinOnlineAutoAssist.Core.Abstractions;

/// <summary>
/// Source of game state. Implementations:
///  - <c>SimulatedGameWorld</c> (NinOnlineAutoAssist.Simulation) — scripted, deterministic.
///  - ReadOnlyGameStateProvider (NinOnlineAutoAssist.Live): targeted passive .NET reads,
///    restricted to a fingerprinted client build and confirmed character identity.
///  - UnavailableGameStateProvider: explicit fallback, never fabricated state.
/// </summary>
public interface IGameStateProvider
{
    /// <summary>Current snapshot, or null when state is unavailable.</summary>
    GameSnapshot? GetSnapshot();

    /// <summary>Human-readable status for diagnostics ("OK", "UNAVAILABLE: reason", ...).</summary>
    string Status { get; }

    /// <summary>True when live data is actually flowing (false for stubs).</summary>
    bool IsLiveData { get; }
}

/// <summary>Result of a requested orientation change.</summary>
public sealed record TurnResult(bool Accepted, string Reason)
{
    public static TurnResult Ok(string reason = "OK") => new(true, reason);
    public static TurnResult Rejected(string reason) => new(false, reason);
}

/// <summary>
/// Abstraction over "turn the character without walking".
/// Implementations: simulator (records/applies turns), or a configurable key-input
/// adapter (NinOnlineAutoAssist.App/Integration) — disabled by default and only
/// usable when explicitly configured and permitted by the environment.
/// </summary>
public interface IOrientationController
{
    TurnResult RequestTurn(Direction direction);
}

/// <summary>Result of a requested combat action (jutsu/attack).</summary>
public sealed record ActionResult(bool Accepted, string Reason)
{
    public bool Retryable { get; init; }
    public static ActionResult Ok(string reason = "OK") => new(true, reason);
    public static ActionResult Rejected(string reason) => new(false, reason);
    public static ActionResult Deferred(string reason) => new(false, reason) { Retryable = true };
}

/// <summary>
/// Abstraction over "press the key bound to an action". Never implies the action
/// connected with the target — see docs: requested vs executed vs confirmed are distinct.
/// </summary>
public interface IActionExecutor
{
    ActionResult Execute(string actionId, string boundKey);
}
