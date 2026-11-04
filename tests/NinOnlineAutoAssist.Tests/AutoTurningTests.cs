using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.AutoTurning;
using NinOnlineAutoAssist.Core.Geometry;
using Xunit;

namespace NinOnlineAutoAssist.Tests;

public class DirectionCalculatorTests
{
    private static readonly DirectionCalculatorOptions Opts = new() { PositionTolerance = 1.0, HysteresisRatio = 1.25 };

    [Theory]
    [InlineData(0, -10, Direction.Up)]
    [InlineData(0, 10, Direction.Down)]
    [InlineData(-10, 0, Direction.Left)]
    [InlineData(10, 0, Direction.Right)]
    public void Cardinal_targets_map_to_cardinal_directions(double dx, double dy, Direction expected)
        => Assert.Equal(expected, DirectionCalculator.Calculate(dx, dy, Direction.None, Opts));

    [Fact]
    public void Both_axes_within_tolerance_returns_None()
        => Assert.Equal(Direction.None, DirectionCalculator.Calculate(0.5, -0.5, Direction.Up, Opts));

    [Fact]
    public void Diagonal_prefers_dominant_axis()
    {
        Assert.Equal(Direction.Right, DirectionCalculator.Calculate(10, -3, Direction.None, Opts));
        Assert.Equal(Direction.Up, DirectionCalculator.Calculate(3, -10, Direction.None, Opts));
    }

    [Fact]
    public void Exact_tie_prefers_current_facing_axis()
    {
        Assert.Equal(Direction.Up, DirectionCalculator.Calculate(10, -10, Direction.Up, Opts));
        Assert.Equal(Direction.Right, DirectionCalculator.Calculate(10, -10, Direction.Right, Opts));
        // No previous facing: documented default is horizontal.
        Assert.Equal(Direction.Right, DirectionCalculator.Calculate(10, -10, Direction.None, Opts));
    }

    [Fact]
    public void Hysteresis_prevents_flip_flop_on_marginal_axis()
    {
        // Dominant by less than the hysteresis ratio: keep current axis.
        Assert.Equal(Direction.Right, DirectionCalculator.Calculate(10, -11, Direction.Right, Opts));
        // Clear dominance: switch allowed.
        Assert.Equal(Direction.Up, DirectionCalculator.Calculate(10, -13, Direction.Right, Opts));
    }
}

public class AutoTurningEngineTests
{
    [Fact]
    public void Target_above_requests_Up() { var rig = NewRig(); rig.World.PlayerFacing = Direction.Down; rig.StandardArena(0, -5); Assert.Equal(TurnDecisionKind.TurnRequested, rig.Tick().Kind); Assert.Equal(Direction.Up, rig.Orientation.History.Last().Direction); }

    [Fact]
    public void Target_below_requests_Down() { var rig = NewRig(); rig.World.PlayerFacing = Direction.Up; rig.StandardArena(0, 5); Assert.Equal(Direction.Down, rig.Tick() is var d && d.Kind == TurnDecisionKind.TurnRequested ? rig.Orientation.History.Last().Direction : Direction.None); }

    [Fact]
    public void Target_left_requests_Left() { var rig = NewRig(); rig.World.PlayerFacing = Direction.Right; rig.StandardArena(-5, 0); rig.Tick(); Assert.Equal(Direction.Left, rig.Orientation.History.Last().Direction); }

    [Fact]
    public void Target_right_requests_Right() { var rig = NewRig(); rig.World.PlayerFacing = Direction.Left; rig.StandardArena(5, 0); rig.Tick(); Assert.Equal(Direction.Right, rig.Orientation.History.Last().Direction); }

    [Fact]
    public void Target_in_motion_tracked_across_updates()
    {
        var rig = NewRig();
        rig.World.PlayerFacing = Direction.Left;
        rig.StandardArena(5, 0);            // initially to the right -> Right
        rig.Tick();
        Assert.Equal(Direction.Right, rig.Orientation.History.Last().Direction);

        rig.Clock.Advance(500);
        rig.World.PlayerFacing = Direction.Right;
        rig.World.MoveTarget("atk", 0, -6);  // now above -> Up
        var decision = rig.Tick();
        Assert.Equal(TurnDecisionKind.TurnRequested, decision.Kind);
        Assert.Equal(Direction.Up, rig.Orientation.History.Last().Direction);
    }

    [Fact]
    public void Target_disappearing_stops_issuing_commands()
    {
        var rig = NewRig();
        rig.World.PlayerFacing = Direction.Down;
        rig.StandardArena(0, -5);
        rig.Tick();
        var count = rig.Orientation.RequestCount;
        rig.World.RemoveTarget("atk");
        var decision = rig.Tick();
        Assert.Equal(TurnDecisionKind.NoTarget, decision.Kind);
        Assert.Equal(count, rig.Orientation.RequestCount); // no new commands
    }

    [Fact]
    public void Rapid_target_switch_handled_without_oscillation()
    {
        var rig = NewRig();
        rig.World.PlayerFacing = Direction.Left;
        rig.StandardArena(5, 0);   // target A to the right
        rig.Tick();
        rig.World.PlayerFacing = Direction.Right;

        rig.World.RemoveTarget("atk");
        rig.World.SpawnTarget("ally-target", -5, 0); // target B to the left
        rig.World.SelectTarget("ally-target");
        var decision = rig.Tick();
        Assert.Equal(TurnDecisionKind.TurnRequested, decision.Kind);
        Assert.Equal(Direction.Left, rig.Orientation.History.Last().Direction);
    }

    [Fact]
    public void Diagonal_target_resolves_to_dominant_axis()
    {
        var rig = NewRig();
        rig.World.PlayerFacing = Direction.Up;
        rig.StandardArena(10, -4);  // mostly horizontal
        rig.Tick();
        Assert.Equal(Direction.Right, rig.Orientation.History.Last().Direction);
    }

    [Fact]
    public void Very_close_coordinates_do_not_emit_commands()
    {
        var rig = NewRig(new AutoTurningOptions { Enabled = true, MinCommandIntervalMs = 0, Geometry = new DirectionCalculatorOptions { PositionTolerance = 2.0 } });
        rig.StandardArena(0.5, -0.5);
        var decision = rig.Tick();
        Assert.Equal(TurnDecisionKind.NoChange, decision.Kind);
        Assert.Empty(rig.Orientation.History);
    }

    [Fact]
    public void Rapid_facing_changes_do_not_spam_duplicate_commands()
    {
        var rig = NewRig(new AutoTurningOptions { Enabled = true, MinCommandIntervalMs = 0 });
        rig.World.PlayerFacing = Direction.Down;
        rig.StandardArena(0, -5);
        rig.Tick();   // requests Up
        rig.World.PlayerFacing = Direction.Up;
        rig.Tick();   // already aligned -> no command
        rig.Tick();   // still aligned -> no command
        Assert.Single(rig.Orientation.History);
    }

    [Fact]
    public void Manual_movement_suppresses_auto_turn_briefly()
    {
        var rig = NewRig();
        rig.StandardArena(0, -5);
        rig.World.PlayerFacing = Direction.Down;
        rig.Tick(); // baseline position recorded (no suppression on first sample)
        rig.Clock.Advance(100);
        rig.World.PlayerX = 3.0; // player moved manually
        var decision = rig.Tick();
        Assert.Equal(TurnDecisionKind.Suppressed, decision.Kind);
    }

    // ── helpers ───────────────────────────────────────────────────────
    private sealed class Rig
    {
        public required Simulation.SimulatedGameWorld World { get; init; }
        public required Simulation.SimulatedClock Clock { get; init; }
        public required Simulation.SimulatedOrientationController Orientation { get; init; }
        public required AutoTurningEngine Engine { get; init; }
        public TurnDecision Tick() => Engine.Update(World.GetSnapshot()!);

        /// <summary>Spawn + select target "atk" at the given position.</summary>
        public void StandardArena(double x, double y)
        {
            World.SpawnTarget("atk", x, y);
            World.SelectTarget("atk");
        }
    }

    private static Rig NewRig(AutoTurningOptions? options = null)
    {
        var clock = new Simulation.SimulatedClock(0);
        var world = new Simulation.SimulatedGameWorld(clock);
        var orientation = new Simulation.SimulatedOrientationController(clock) { FacingSink = d => world.PlayerFacing = d };
        var engine = new AutoTurningEngine(orientation, clock, options: options ?? new AutoTurningOptions { Enabled = true, MinCommandIntervalMs = 0 });
        return new Rig { World = world, Clock = clock, Orientation = orientation, Engine = engine };
    }
}
