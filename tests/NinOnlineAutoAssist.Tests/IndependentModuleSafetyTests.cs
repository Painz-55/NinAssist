using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.AutoTurning;
using NinOnlineAutoAssist.Core.Geometry;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Simulation;

namespace NinOnlineAutoAssist.Tests;

public sealed class IndependentModuleSafetyTests
{
    private static LiveFrame Frame(long time = 0, bool active = false, int? duration = null) => new(
        new GameSnapshot { Player = new PlayerState { Id = "local", Facing = Direction.Down },
            Target = new TargetInfo { Id = "target", PositionY = -3, IsTargeted = true },
            ActiveBuffs = active ? new[] { "Substitution Technique" } : Array.Empty<string>() },
        time, 51, "Player", "Target", active, duration, 0);

    [Theory]
    [InlineData(true, true, true, false, false, true)]
    [InlineData(true, false, true, true, true, false)]
    [InlineData(true, true, false, false, false, false)]
    [InlineData(false, true, true, true, false, true)]
    [InlineData(false, false, true, false, false, false)]
    [InlineData(true, true, true, true, true, true)]
    [InlineData(true, true, false, true, true, false)]
    public void ModulesCannotBorrowEachOthersPermission(bool turnOn, bool punishOn, bool authorized,
        bool ctrl, bool expectedTurn, bool expectedPunish)
    {
        var frame = Frame();
        Assert.Equal(expectedTurn, LiveSafetyGate.CheckTurning(frame, 0, turnOn, true, ctrl, false, false, false).Allowed);
        Assert.Equal(expectedPunish, LiveSafetyGate.CheckPunish(frame, 0, punishOn, authorized, true, false, false, false).Allowed);
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, false, false, true)]
    public void CommonGuardsRemainButManualMovementDoesNotBlock(bool foreground, bool emergency, bool movement, bool text)
    {
        var expected = foreground && !emergency && !text;
        Assert.Equal(expected, LiveSafetyGate.CheckPunish(Frame(), 0, true, true, foreground, emergency, movement, text).Allowed);
        Assert.Equal(expected, LiveSafetyGate.CheckTurning(Frame(), 0, true, foreground, true, emergency, movement, text).Allowed);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("stale")]
    [InlineData("slow")]
    [InlineData("dead")]
    [InlineData("no-target")]
    [InlineData("dead-target")]
    [InlineData("not-selected")]
    public void OnlyTurningRequiresSelectedTarget(string invalid)
    {
        LiveFrame? frame = Frame();
        frame = invalid switch
        {
            "missing" => null,
            "stale" => frame with { CapturedAtMs = -501 },
            "slow" => frame with { ReadDurationMs = 501 },
            "dead" => frame with { Snapshot = frame.Snapshot with { Player = frame.Snapshot.Player with { IsAlive = false } } },
            "no-target" => frame with { Snapshot = frame.Snapshot with { Target = null } },
            "dead-target" => frame with { Snapshot = frame.Snapshot with { Target = frame.Snapshot.Target! with { IsAlive = false } } },
            _ => frame with { Snapshot = frame.Snapshot with { Target = frame.Snapshot.Target! with { IsTargeted = false } } }
        };
        Assert.Equal(invalid is "no-target" or "dead-target" or "not-selected",
            LiveSafetyGate.CheckPunish(frame, 0, true, true, true, false, false, false).Allowed);
        Assert.False(LiveSafetyGate.CheckTurning(frame, 0, true, true, true, false, false, false).Allowed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PunishCompletesWithoutCtrlEvenWhenTurnIsOn(bool initiallyHoldCtrl)
    {
        var clock = new SimulatedClock(0);
        var executor = new SimulatedActionExecutor(clock);
        var orientation = new SimulatedOrientationController(clock);
        var punish = new EarlyBuffPunishEngine(executor, clock);
        var turning = new AutoTurningEngine(orientation, clock, options: new AutoTurningOptions
        {
            ManualMovementPriority = false, MinCommandIntervalMs = 0,
            Geometry = new DirectionCalculatorOptions { PositionTolerance = 0.1 }
        });
        void Tick(long time, bool active, int? duration, bool ctrl)
        {
            clock.Advance(time - clock.NowMs);
            var frame = Frame(time, active, duration);
            var turnGate = LiveSafetyGate.CheckTurning(frame, time, true, true, ctrl, false, false, false);
            var punishGate = LiveSafetyGate.CheckPunish(frame, time, true, true, true, false, false, false);
            turning.UpdateOptions(turning.Options with { Enabled = turnGate.Allowed });
            punish.Update(frame, true, true, punishGate.Allowed);
            if (!punish.BlocksAutoTurning) turning.Update(frame.Snapshot);
        }
        Tick(0, true, 7, initiallyHoldCtrl);
        var initialTurns = orientation.RequestCount;
        Tick(200, false, null, false);
        Tick(400, false, null, false);
        Tick(520, false, null, false);
        Tick(640, false, null, false);
        Assert.Equal(new[] { "A", "Q", "S" }, executor.History.Select(a => a.Key));
        Assert.Equal(EarlyPunishState.Completed, punish.State);
        Assert.Equal(initiallyHoldCtrl ? 1 : 0, initialTurns);
        Assert.Equal(initialTurns, orientation.RequestCount); // Sub permission never authorizes a turn without Ctrl.
        Tick(800, false, null, true);
        Assert.Equal(initialTurns + 1, orientation.RequestCount);
        Assert.Equal(3, executor.History.Count);
    }
}
