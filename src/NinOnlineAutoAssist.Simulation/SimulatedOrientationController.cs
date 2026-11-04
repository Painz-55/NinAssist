using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;

namespace NinOnlineAutoAssist.Simulation;

/// <summary>
/// Records requested turns and (optionally) applies them instantly to its own state.
/// Never sends real input.
/// </summary>
public sealed class SimulatedOrientationController : IOrientationController
{
    private readonly List<(long AtMs, Direction Direction)> _history = new();
    private readonly IMonotonicClock _clock;
    public bool RejectNextRequest { get; set; }
    public string RejectReason { get; set; } = "Simulated rejection";
    public int RequestCount => _history.Count;

    /// <summary>When set, the player's simulated facing is updated on each accepted request.</summary>
    public Func<Direction, Direction>? FacingSink { get; set; }

    public SimulatedOrientationController(IMonotonicClock clock) => _clock = clock;

    public IReadOnlyList<(long AtMs, Direction Direction)> History => _history;

    public TurnResult RequestTurn(Direction direction)
    {
        if (RejectNextRequest)
        {
            RejectNextRequest = false;
            return TurnResult.Rejected(RejectReason);
        }
        _history.Add((_clock.NowMs, direction));
        FacingSink?.Invoke(direction);
        return TurnResult.Ok("Simulated turn accepted");
    }
}

/// <summary>
/// Records requested actions. Does NOT emulate a connected hit — confirmation would
/// require an explicit ActionConfirmed event injected by the scenario.
/// </summary>
public sealed class SimulatedActionExecutor : IActionExecutor
{
    private readonly IMonotonicClock _clock;
    private readonly List<(long AtMs, string ActionId, string Key)> _history = new();
    public bool RejectNextRequest { get; set; }
    public string RejectReason { get; set; } = "Simulated rejection";

    public SimulatedActionExecutor(IMonotonicClock clock) => _clock = clock;

    public IReadOnlyList<(long AtMs, string ActionId, string Key)> History => _history;

    public ActionResult Execute(string actionId, string boundKey)
    {
        if (RejectNextRequest)
        {
            RejectNextRequest = false;
            return ActionResult.Rejected(RejectReason);
        }
        _history.Add((_clock.NowMs, actionId, boundKey));
        return ActionResult.Ok("Simulated action dispatched");
    }
}
