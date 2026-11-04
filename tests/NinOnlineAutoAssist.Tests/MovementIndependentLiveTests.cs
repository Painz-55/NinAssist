using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.AutoTurning;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Simulation;

namespace NinOnlineAutoAssist.Tests;

public sealed class MovementIndependentLiveTests
{
    private static LiveFrame Frame(long time, string? buff = null) => new(
        new GameSnapshot
        {
            Player = new PlayerState { Id = "local", PositionX = time / 10.0, Facing = Direction.Down },
            Target = new TargetInfo { Id = "target", IsTargeted = true, PositionY = -8 }
        }, time, 51, "Player", "Target", buff is not null, buff is null ? null : 5, 0)
        { ObservedActiveBuffs = buff is null ? Array.Empty<ObservedBuff>() : new[] { new ObservedBuff(buff, 5) } };

    [Theory]
    [InlineData(SubstitutionBuffs.Normal)]
    [InlineData(SubstitutionBuffs.Water)]
    [InlineData(SubstitutionBuffs.Tai)]
    public void MovementDoesNotCancelBuffTrackingOrRemainingComboKeys(string buff)
    {
        var clock = new SimulatedClock(0);
        var executor = new SimulatedActionExecutor(clock);
        var engine = new EarlyBuffPunishEngine(executor, clock, new EarlyPunishOptions
        { FirstToSecondMs = 100, SecondToThirdMs = 200 });
        foreach (var time in new long[] { 0, 50, 100, 150, 200, 300, 400 })
        {
            clock.Advance(time - clock.NowMs);
            var frame = Frame(time, time == 0 ? buff : null);
            var gate = LiveSafetyGate.CheckPunish(frame, time, true, true, true, false, true, false);
            Assert.True(gate.Allowed); // physical movement is held throughout; position also changes each sample
            engine.Update(frame, true, true, gate.Allowed);
        }
        Assert.Equal(new[] { "A", "Q", "S" }, executor.History.Select(h => h.Key));
        Assert.Equal(new long[] { 100, 200, 400 }, executor.History.Select(h => h.AtMs));
        Assert.Equal(EarlyPunishState.Completed, engine.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalAndAutomaticSubTurningContinueWhileWalking(bool subWindow)
    {
        var clock = new SimulatedClock(0);
        var orientation = new SimulatedOrientationController(clock);
        var engine = new AutoTurningEngine(orientation, clock, options: new AutoTurningOptions
        { ManualMovementPriority = false, MinCommandIntervalMs = 40 });
        foreach (var time in new long[] { 0, 100 })
        {
            clock.Advance(time - clock.NowMs);
            var frame = Frame(time, subWindow ? SubstitutionBuffs.Tai : null);
            var gate = subWindow
                ? LiveSafetyGate.CheckSubstitutionTurning(frame, time, true, true, true, true, false, true, false)
                : LiveSafetyGate.CheckTurning(frame, time, true, true, true, false, true, false);
            Assert.True(gate.Allowed);
            engine.UpdateOptions(engine.Options with { Enabled = gate.Allowed });
            Assert.Equal(TurnDecisionKind.TurnRequested, engine.Update(frame.Snapshot).Kind);
        }
        Assert.Equal(2, orientation.RequestCount);
    }

    [Theory]
    [InlineData("foreground")]
    [InlineData("emergency")]
    [InlineData("text")]
    [InlineData("stale")]
    [InlineData("dead")]
    public void WalkingDoesNotBypassRemainingGuards(string invalid)
    {
        var frame = Frame(0, SubstitutionBuffs.Tai);
        if (invalid == "dead") frame = frame with { Snapshot = frame.Snapshot with { Player = frame.Snapshot.Player with { IsAlive = false } } };
        var now = invalid == "stale" ? 501 : 0;
        var foreground = invalid != "foreground";
        var emergency = invalid == "emergency";
        var text = invalid == "text";
        Assert.False(LiveSafetyGate.CheckPunish(frame, now, true, true, foreground, emergency, true, text).Allowed);
        Assert.False(LiveSafetyGate.CheckTurning(frame, now, true, foreground, true, emergency, true, text).Allowed);
        Assert.False(LiveSafetyGate.CheckSubstitutionTurning(frame, now, true, true, true, foreground, emergency, true, text).Allowed);
    }
}
