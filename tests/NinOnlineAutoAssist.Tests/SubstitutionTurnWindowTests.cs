using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.AutoTurning;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Simulation;

namespace NinOnlineAutoAssist.Tests;

public sealed class SubstitutionTurnWindowTests
{
    private static LiveFrame Frame(long at, bool active = false) => new(
        new GameSnapshot
        {
            Player = new PlayerState { Id = "local", Facing = Direction.Down },
            Target = new TargetInfo { Id = "target", IsTargeted = true, PositionY = -5 }
        }, at, 51, "Player", "Target", active, active ? 7 : null, 0);

    [Fact]
    public void OpensDuringBuffAndClosesExactlyOneSecondAfterFirstAbsentSample()
    {
        var window = new SubstitutionTurnWindow();
        window.Update(Frame(0, true), 0, true);
        Assert.True(window.IsOpen(Frame(0, true), 0));
        for (var time = 200; time <= 2000; time += 200)
        {
            window.Update(Frame(time, true), time, true);
            Assert.True(window.IsOpen(Frame(time, true), time));
        }
        // Countdown duration does not restrict turning: natural expiry also gets the one-second tail.
        for (var time = 2200; time <= 3000; time += 200)
        {
            window.Update(Frame(time), time, true);
            Assert.True(window.IsOpen(Frame(time), time));
        }
        window.Update(Frame(3199), 3199, true);
        Assert.True(window.IsOpen(Frame(3199), 3199));
        Assert.False(window.IsOpen(Frame(3199), 3200));
        window.Update(Frame(3200), 3200, true);
        Assert.False(window.IsOpen(Frame(3200), 3200));
    }

    [Fact]
    public void AbsentSamplesAndDuplicateSamplesDoNotExtendTail()
    {
        var window = new SubstitutionTurnWindow();
        window.Update(Frame(0, true), 0, true);
        window.Update(Frame(100), 100, true);
        window.Update(Frame(100), 300, true);
        window.Update(Frame(500), 500, true);
        window.Update(Frame(900), 900, true);
        Assert.True(window.IsOpen(Frame(900), 1099));
        Assert.False(window.IsOpen(Frame(900), 1100));
    }

    [Fact]
    public void NeverOpensWithoutAnObservedActiveBuff()
    {
        var window = new SubstitutionTurnWindow();
        for (var time = 0; time <= 2000; time += 200)
        {
            window.Update(Frame(time), time, true);
            Assert.False(window.IsOpen(Frame(time), time));
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("stale")]
    [InlineData("slow")]
    [InlineData("map")]
    [InlineData("identity")]
    [InlineData("dead")]
    [InlineData("gap")]
    [InlineData("disabled")]
    public void InvalidContinuityOrPermissionCancelsWindow(string change)
    {
        var window = new SubstitutionTurnWindow();
        window.Update(Frame(0, true), 0, true);
        LiveFrame? frame = Frame(100);
        var now = 100L;
        switch (change)
        {
            case "missing": frame = null; break;
            case "stale": now = 601; break;
            case "slow": frame = frame with { ReadDurationMs = 501 }; break;
            case "map": frame = frame with { Map = 52 }; break;
            case "identity": frame = frame with { Snapshot = frame.Snapshot with { Player = frame.Snapshot.Player with { Id = "other" } } }; break;
            case "dead": frame = frame with { Snapshot = frame.Snapshot with { Player = frame.Snapshot.Player with { IsAlive = false } } }; break;
            case "gap": now = 601; frame = Frame(now); break;
        }
        window.Update(frame, now, change != "disabled");
        Assert.False(window.IsOpen(frame, now));
        window.Update(Frame(now + 100), now + 100, true);
        Assert.False(window.IsOpen(Frame(now + 100), now + 100));
    }

    [Fact]
    public void TurningRequiresTargetButWindowAndPunishDoNot()
    {
        var window = new SubstitutionTurnWindow();
        var active = Frame(0, true) with { Snapshot = Frame(0, true).Snapshot with { Target = null } };
        window.Update(active, 0, true);
        Assert.True(window.IsOpen(active, 0));
        Assert.False(LiveSafetyGate.CheckSubstitutionTurning(active, 0, true, true, true, true, false, false, false).Allowed);
        Assert.True(LiveSafetyGate.CheckPunish(active, 0, true, true, true, false, false, false).Allowed);
        var selected = active with { Snapshot = active.Snapshot with { Target = Frame(0).Snapshot.Target } };
        Assert.True(LiveSafetyGate.CheckSubstitutionTurning(selected, 0, true, true, true, true, false, false, false).Allowed);
    }

    [Theory]
    [InlineData(false, true, true, true, false, false, false)]
    [InlineData(true, false, true, true, false, false, false)]
    [InlineData(true, true, false, true, false, false, false)]
    [InlineData(true, true, true, false, false, false, false)]
    [InlineData(true, true, true, true, true, false, false)]
    [InlineData(true, true, true, true, false, true, false)]
    [InlineData(true, true, true, true, false, false, true)]
    public void AutomaticWindowRetainsGuardsExceptMovementPriority(bool enabled, bool authorized, bool windowOpen,
        bool foreground, bool emergency, bool movement, bool text) =>
        Assert.Equal(enabled && authorized && windowOpen && foreground && !emergency && !text,
            LiveSafetyGate.CheckSubstitutionTurning(Frame(0, true), 0, enabled, authorized, windowOpen,
            foreground, emergency, movement, text).Allowed);

    [Fact]
    public void AutomaticSubTurnDoesNotRequireRegularModuleOrHeldKey()
    {
        var clock = new SimulatedClock(0);
        var orientation = new SimulatedOrientationController(clock);
        var turning = new AutoTurningEngine(orientation, clock, options: new AutoTurningOptions
        { ManualMovementPriority = false, MinCommandIntervalMs = 40 });
        var window = new SubstitutionTurnWindow();
        foreach (var (at, active) in new[] { (0, true), (100, false), (500, false), (900, false), (1100, false) })
        {
            clock.Advance(at - clock.NowMs);
            var frame = Frame(at, active);
            window.Update(frame, at, true);
            var regular = LiveSafetyGate.CheckTurning(frame, at, false, true, false, false, false, false);
            Assert.False(regular.Allowed);
            var sub = LiveSafetyGate.CheckSubstitutionTurning(frame, at, true, true, window.IsOpen(frame, at), true, false, false, false);
            turning.UpdateOptions(turning.Options with { Enabled = sub.Allowed });
            turning.Update(frame.Snapshot);
        }
        Assert.Equal(4, orientation.RequestCount); // no extra command at exactly +1000 ms
    }

    [Fact]
    public void TurnFitsBetweenComboKeysButYieldsWhenNextKeyIsImminent()
    {
        var clock = new SimulatedClock(0);
        var executor = new SimulatedActionExecutor(clock);
        var punish = new EarlyBuffPunishEngine(executor, clock, new EarlyPunishOptions
        { FirstToSecondMs = 0, SecondToThirdMs = 200, TriggerDelayMs = 300 });
        punish.Update(Frame(0, true), true, true, true);
        clock.Advance(50); punish.Update(Frame(50), true, true, true);
        clock.Advance(50); punish.Update(Frame(100), true, true, true);
        Assert.True(punish.HasTimeForTurn);
        Assert.Equal(400, punish.NextKeyDueAtMs);
        clock.Advance(240);
        Assert.False(punish.HasTimeForTurn);
        clock.Advance(60); punish.Update(Frame(400), true, true, true);
        Assert.Equal(new[] { "A", "Q" }, executor.History.Select(h => h.Key));
        Assert.True(punish.HasTimeForTurn);
        clock.Advance(140);
        Assert.False(punish.HasTimeForTurn);
        clock.Advance(60); punish.Update(Frame(600), true, true, true);
        Assert.Equal(3, executor.History.Count);
        Assert.True(punish.HasTimeForTurn);
        Assert.Null(punish.NextKeyDueAtMs);
    }

    [Theory]
    [InlineData(0, -8, Direction.Up)]
    [InlineData(0, 8, Direction.Down)]
    [InlineData(-8, 0, Direction.Left)]
    [InlineData(8, 0, Direction.Right)]
    public void AutomaticWindowFacesCurrentTargetInAllDirections(double x, double y, Direction expected)
    {
        var clock = new SimulatedClock(0);
        var orientation = new SimulatedOrientationController(clock);
        var frame = Frame(0, true);
        frame = frame with { Snapshot = frame.Snapshot with
        {
            Player = frame.Snapshot.Player with { Facing = Direction.None },
            Target = frame.Snapshot.Target! with { PositionX = x, PositionY = y }
        } };
        var window = new SubstitutionTurnWindow();
        window.Update(frame, 0, true);
        var gate = LiveSafetyGate.CheckSubstitutionTurning(frame, 0, true, true, window.IsOpen(frame, 0), true, false, false, false);
        var engine = new AutoTurningEngine(orientation, clock, options: new AutoTurningOptions
        { Enabled = gate.Allowed, ManualMovementPriority = false });
        engine.Update(frame.Snapshot);
        Assert.Equal(expected, Assert.Single(orientation.History).Direction);
    }

    [Fact]
    public void SubTeleportDoesNotCausePositionBasedManualSuppressionInAutomaticWindow()
    {
        var clock = new SimulatedClock(0);
        var orientation = new SimulatedOrientationController(clock);
        var engine = new AutoTurningEngine(orientation, clock, options: new AutoTurningOptions
        { Enabled = true, ManualMovementPriority = false, MinCommandIntervalMs = 40 });
        engine.Update(Frame(0, true).Snapshot);
        clock.Advance(100);
        var after = Frame(100).Snapshot;
        after = after with { Player = after.Player with { PositionX = 20 } };
        var result = engine.Update(after);
        Assert.Equal(TurnDecisionKind.TurnRequested, result.Kind);
        Assert.Equal(2, orientation.RequestCount);
        Assert.True(LiveSafetyGate.CheckSubstitutionTurning(Frame(100), 100, true, true, true, true, false, true, false).Allowed);
    }
}
