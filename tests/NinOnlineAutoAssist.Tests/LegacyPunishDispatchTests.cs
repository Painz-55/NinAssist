using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Simulation;

namespace NinOnlineAutoAssist.Tests;

public sealed class LegacyPunishDispatchTests
{
    private sealed class Executor(Func<string, ActionResult> execute) : IActionExecutor
    { public ActionResult Execute(string actionId, string key) => execute(key); }
    private static LiveFrame Frame(long at, bool buff) => new(new GameSnapshot
    { Player = new PlayerState { Id = "local" }, Target = null }, at, 51, "Local", null, buff, buff ? 5 : null, 0);
    private static void Trigger(ILivePunishEngine engine, SimulatedClock clock)
    {
        engine.Update(Frame(0, true), true, true, true);
        clock.Advance(40); engine.Update(Frame(40, false), true, true, true);
        clock.Advance(40); engine.Update(Frame(80, false), true, true, true);
    }

    [Fact]
    public void LegacyZeroIntervalsPreservePhysicalKeyHoldsWithoutExtraWorkerTick()
    {
        var clock = new SimulatedClock(0); var downs = new List<long>();
        var engine = LivePunishFactory.Create(LivePunishMode.Legacy,
            new Executor(_ => { downs.Add(clock.NowMs); clock.Advance(40); return ActionResult.Ok(); }),
            clock, new() { FirstToSecondMs = 0, SecondToThirdMs = 0 });
        Trigger(engine, clock);
        Assert.Equal(new long[] { 80, 120, 160 }, downs);
        Assert.Equal(EarlyPunishState.Completed, engine.State);
    }

    [Fact]
    public void RetiredNewModeCannotBeInstantiatedOrSendInput()
    {
        var clock = new SimulatedClock(0); var sent = 0;
        Assert.Throws<ArgumentException>(() => LivePunishFactory.Create(LivePunishMode.AnchoredExperimental,
            new Executor(_ => { sent++; return ActionResult.Ok(); }), clock, new()));
        Assert.Equal(0, sent);
    }
}
