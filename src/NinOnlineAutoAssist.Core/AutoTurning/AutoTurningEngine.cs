using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Geometry;
using NinOnlineAutoAssist.Core.Logging;
using NinOnlineAutoAssist.Core.Models;

namespace NinOnlineAutoAssist.Core.AutoTurning;

public sealed record AutoTurningOptions
{
    public bool Enabled { get; init; } = false;
    /// <summary>Minimum delay between two turn commands (ms).</summary>
    public long MinCommandIntervalMs { get; init; } = 180;
    /// <summary>Suspend auto-turn while the player is casting/in an animation lock.</summary>
    public bool SuspendDuringCasting { get; init; } = true;
    /// <summary>When true, manual movement input suppresses auto-turn briefly.</summary>
    public bool ManualMovementPriority { get; init; } = true;
    /// <summary>How long (ms) auto-turn stays suppressed after manual movement is detected.</summary>
    public long ManualOverrideDurationMs { get; init; } = 800;
    /// <summary>Movement delta (units) between ticks that counts as manual movement.</summary>
    public double ManualMovementThreshold { get; init; } = 2.0;
    public DirectionCalculatorOptions Geometry { get; init; } = DirectionCalculatorOptions.Default;
    /// <summary>Verbose decision logging.</summary>
    public bool DiagnosticMode { get; init; } = false;
}

public enum TurnDecisionKind { NoChange, TurnRequested, TurnRejected, Suspended, Suppressed, NoTarget }

public sealed record TurnDecision(TurnDecisionKind Kind, Direction Desired, string Reason);

/// <summary>
/// Decides and (optionally) requests facing corrections towards the selected target.
/// Pure decision logic + injected side-effecting controller; never walks the character.
/// </summary>
public sealed class AutoTurningEngine
{
    private readonly IOrientationController _controller;
    private readonly IMonotonicClock _clock;
    private readonly IAutoAssistLogger _log;
    private AutoTurningOptions _options;

    private long? _lastCommandAtMs;
    private (double X, double Y)? _lastPosition;
    private long _manualSuppressionUntilMs;

    public AutoTurningEngine(IOrientationController controller, IMonotonicClock clock,
        IAutoAssistLogger? logger = null, AutoTurningOptions? options = null)
    {
        _controller = controller;
        _clock = clock;
        _log = logger ?? NullLogger.Instance;
        _options = options ?? new AutoTurningOptions();
    }

    public AutoTurningOptions Options => _options;
    public void UpdateOptions(AutoTurningOptions options) => _options = options;

    /// <summary>
    /// Call on every tick. Returns the decision taken (for UI/diagnostics/tests).
    /// </summary>
    public TurnDecision Update(GameSnapshot snapshot)
    {
        var now = _clock.NowMs;

        if (!_options.Enabled)
            return new(TurnDecisionKind.Suspended, Direction.None, "AutoTurning disabled");

        var player = snapshot.Player;
        if (!player.IsAlive)
            return new(TurnDecisionKind.Suspended, Direction.None, "Player is dead");

        if (_options.SuspendDuringCasting && (player.IsCasting || player.IsActionBlocked))
            return new(TurnDecisionKind.Suspended, Direction.None, "Player is casting/action-blocked");

        // Manual movement detection (position changed without our intervention).
        DetectManualMovement(snapshot, now);
        if (_options.ManualMovementPriority && now < _manualSuppressionUntilMs)
            return new(TurnDecisionKind.Suppressed, Direction.None, "Manual movement priority (override active)");

        var target = snapshot.Target;
        if (target is null || !target.IsAlive || !target.IsTargeted)
            return new(TurnDecisionKind.NoTarget, Direction.None, "No valid targeted target");

        double dx = target.PositionX - player.PositionX;
        double dy = target.PositionY - player.PositionY;

        var desired = DirectionCalculator.Calculate(dx, dy, player.Facing, _options.Geometry);
        if (desired == Direction.None)
            return new(TurnDecisionKind.NoChange, desired, "Within tolerance / holding facing");

        if (desired == player.Facing)
            return new(TurnDecisionKind.NoChange, desired, "Already facing target");

        if (_lastCommandAtMs is { } lastCmd && now - lastCmd < _options.MinCommandIntervalMs)
            return new(TurnDecisionKind.Suppressed, desired, "Min command interval not elapsed");

        var result = _controller.RequestTurn(desired);
        _lastCommandAtMs = now;

        if (_options.DiagnosticMode)
            _log.Log(LogLevel.Debug,
                $"AutoTurn dx={dx:F2} dy={dy:F2} facing={player.Facing} -> {desired} ({result.Reason})",
                Events.EventOrigin.Requested);

        return result.Accepted
            ? new(TurnDecisionKind.TurnRequested, desired, result.Reason)
            : new(TurnDecisionKind.TurnRejected, desired, result.Reason);
    }

    private void DetectManualMovement(GameSnapshot snapshot, long now)
    {
        var pos = (snapshot.Player.PositionX, snapshot.Player.PositionY);
        if (_lastPosition is { } last)
        {
            double moved = DirectionCalculator.Distance(pos.PositionX - last.X, pos.PositionY - last.Y);
            if (moved > _options.ManualMovementThreshold && _options.ManualMovementPriority)
                _manualSuppressionUntilMs = now + _options.ManualOverrideDurationMs;
        }
        _lastPosition = pos;
    }

    /// <summary>Test hook: clears internal cadence/suppression state.</summary>
    public void ResetCadence()
    {
        _lastCommandAtMs = null;
        _manualSuppressionUntilMs = 0;
        _lastPosition = null;
    }
}
