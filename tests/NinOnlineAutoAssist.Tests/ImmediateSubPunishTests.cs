using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Simulation;

namespace NinOnlineAutoAssist.Tests;

public sealed class ImmediateSubPunishTests
{
    private sealed class Executor(Func<string, ActionResult> execute) : IActionExecutor
    { public ActionResult Execute(string actionId, string key) => execute(key); }
    private static LiveFrame Frame(long at, bool buff, double x) => new(new GameSnapshot
    {
        Player = new PlayerState { Id = "local", PositionX = x, PositionY = 5 }
    }, at, 51, "Local", null, buff, buff ? 5 : null, 0)
    { ObservedActiveBuffs = buff ? new[] { new ObservedBuff(SubstitutionBuffs.Normal, 5) } : Array.Empty<ObservedBuff>() };
    private static void Trigger(ILivePunishEngine engine, SimulatedClock clock, LivePunishMode mode)
    {
        engine.Update(Frame(0, true, 10), true, true, true);
        clock.Advance(40); engine.Update(Frame(40, false, 12), true, true, true);
        if (mode == LivePunishMode.Legacy) { clock.Advance(40); engine.Update(Frame(80, false, 12), true, true, true); }
    }

    [Theory]
    [InlineData(LivePunishMode.Legacy)] [InlineData(LivePunishMode.AnchoredExperimental)]
    public void ZeroTransitionDelaysNeverAddAnExtraWorkerTickButRetainPhysicalHolds(LivePunishMode mode)
    {
        var clock = new SimulatedClock(0); var down = new List<long>();
        var engine = LivePunishFactory.Create(mode, new Executor(_ => { down.Add(clock.NowMs); clock.Advance(40); return ActionResult.Ok(); }),
            clock, new() { FirstToSecondMs = 0, SecondToThirdMs = 0 });
        Trigger(engine, clock, mode);
        var triggerAt = mode == LivePunishMode.Legacy ? 80 : 40;
        Assert.Equal(new long[] { triggerAt, triggerAt + 40, triggerAt + 80 }, down);
        Assert.Equal(EarlyPunishState.Completed, engine.State); Assert.Null(engine.NextKeyDueAtMs);
        engine.Update(Frame(clock.NowMs, false, 12), true, true, true); Assert.Equal(3, down.Count);
    }

    [Theory]
    [InlineData(LivePunishMode.Legacy)] [InlineData(LivePunishMode.AnchoredExperimental)]
    public void NonzeroTransitionStillWaitsForConfiguredDeadlineAndZeroThirdDoesNotWaitAgain(LivePunishMode mode)
    {
        var clock = new SimulatedClock(0); var sent = new List<string>();
        var engine = LivePunishFactory.Create(mode, new Executor(key => { sent.Add(key); return ActionResult.Ok(); }),
            clock, new() { FirstToSecondMs = 10, SecondToThirdMs = 0 });
        Trigger(engine, clock, mode); Assert.Equal(new[] { "A" }, sent);
        var due = Assert.IsType<long>(engine.NextKeyDueAtMs); Assert.Equal(clock.NowMs + 10, due);
        clock.Advance(9); engine.Update(Frame(clock.NowMs, false, 12), true, true, true); Assert.Single(sent);
        clock.Advance(1); engine.Update(Frame(clock.NowMs, false, 12), true, true, true);
        Assert.Equal(new[] { "A", "Q", "S" }, sent);
    }

    [Theory]
    [InlineData(LivePunishMode.Legacy)] [InlineData(LivePunishMode.AnchoredExperimental)]
    public void NativeAdapterCanStillRevokeThirdKeyEvenInSameWorkerStep(LivePunishMode mode)
    {
        var clock = new SimulatedClock(0); var accepted = new List<string>(); var allowed = true;
        var engine = LivePunishFactory.Create(mode, new Executor(key =>
        {
            if (!allowed) return ActionResult.Rejected("safety revoked at native dispatch");
            accepted.Add(key); if (key == "Q") allowed = false;
            return ActionResult.Ok();
        }), clock, new() { FirstToSecondMs = 0, SecondToThirdMs = 0 });
        Trigger(engine, clock, mode);
        Assert.Equal(new[] { "A", "Q" }, accepted); Assert.Equal(EarlyPunishState.Cancelled, engine.State);
    }
}
