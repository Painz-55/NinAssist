using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Simulation;

namespace NinOnlineAutoAssist.Tests;

public sealed class PunishTriggerDelayTests
{
    private static LiveFrame Frame(long at, bool buff = false, int duration = 7) => new(
        new GameSnapshot { Player = new PlayerState { Id = "local" }, Target = null },
        at, 51, "Player", null, buff, buff ? duration : null, 0);

    [Theory]
    [InlineData(0)]
    [InlineData(300)]
    [InlineData(2500)]
    public void DelayStartsAfterConfirmationAndDoesNotConsumeComboTimeout(int delay)
    {
        var clock = new SimulatedClock(0);
        var executor = new SimulatedActionExecutor(clock);
        var engine = new EarlyBuffPunishEngine(executor, clock, new EarlyPunishOptions
        { TriggerDelayMs = delay, FirstToSecondMs = 100, SecondToThirdMs = 200 });
        void At(long time, bool buff = false)
        {
            clock.Advance(time - clock.NowMs);
            engine.Update(Frame(time, buff), true, true, true);
        }
        At(0, true); At(50);
        Assert.Empty(executor.History);
        At(100);
        if (delay > 0)
        {
            Assert.Empty(executor.History);
            Assert.Contains("DEBUG", engine.Status);
            Assert.True(engine.BlocksAutoTurning);
            for (var at = 350; at < 100 + delay; at += 250) At(at);
            At(100 + delay - 1);
            Assert.Empty(executor.History);
            At(100 + delay);
        }
        Assert.Equal("A", Assert.Single(executor.History).Key);
        Assert.Equal(100 + delay, executor.History[0].AtMs);
        At(200 + delay); At(400 + delay);
        Assert.Equal(new[] { "A", "Q", "S" }, executor.History.Select(h => h.Key));
        Assert.Equal(EarlyPunishState.Completed, engine.State);
        At(450 + delay);
        Assert.Equal(3, executor.History.Count);
    }

    [Theory]
    [InlineData("off")]
    [InlineData("unauthorized")]
    [InlineData("safety")]
    [InlineData("buff")]
    [InlineData("stale")]
    [InlineData("map")]
    public void PendingDelayRemainsCancellableAndNeverReplays(string guard)
    {
        var clock = new SimulatedClock(0);
        var executor = new SimulatedActionExecutor(clock);
        var engine = new EarlyBuffPunishEngine(executor, clock, new EarlyPunishOptions { TriggerDelayMs = 300 });
        engine.Update(Frame(0, true), true, true, true);
        clock.Advance(50); engine.Update(Frame(50), true, true, true);
        clock.Advance(50); engine.Update(Frame(100), true, true, true);
        clock.Advance(guard == "stale" ? 501 : 100);
        var frame = guard == "stale" ? Frame(100) : Frame(clock.NowMs, guard == "buff");
        if (guard == "map") frame = frame with { Map = 52 };
        engine.Update(frame, guard != "off", guard != "unauthorized", guard != "safety");
        Assert.Empty(executor.History);
        Assert.Equal(EarlyPunishState.Cancelled, engine.State);
        clock.Advance(100); engine.Update(Frame(clock.NowMs), true, true, true);
        Assert.Empty(executor.History);
    }

    [Fact]
    public void DelayDoesNotMakeNaturalExpiryATrigger()
    {
        var clock = new SimulatedClock(0);
        var executor = new SimulatedActionExecutor(clock);
        var engine = new EarlyBuffPunishEngine(executor, clock, new EarlyPunishOptions { TriggerDelayMs = 300 });
        engine.Update(Frame(0, true, 1), true, true, true);
        for (var time = 200; time <= 1200; time += 200)
        {
            clock.Advance(200);
            engine.Update(Frame(time), true, true, true);
        }
        Assert.Empty(executor.History);
        Assert.Equal(EarlyPunishState.Expired, engine.State);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10001)]
    public void InvalidDelayIsRejected(int delay) => Assert.Throws<ArgumentException>(() =>
        new EarlyBuffPunishEngine(new SimulatedActionExecutor(new SimulatedClock(0)), new SimulatedClock(0),
            new EarlyPunishOptions { TriggerDelayMs = delay }));

    [Fact]
    public void DebugDelayIsPersisted()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ninautoassist-delay-{Guid.NewGuid():N}.json");
        try
        {
            ConfigLoader.Save(path, new AutoAssistConfig { Live = new() { PunishTriggerDelayMs = 350 } });
            Assert.Equal(350, ConfigLoader.Load(path).Live.PunishTriggerDelayMs);
        }
        finally { File.Delete(path); }
    }
}
