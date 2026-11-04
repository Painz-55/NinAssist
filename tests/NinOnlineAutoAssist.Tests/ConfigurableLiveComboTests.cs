using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Simulation;

namespace NinOnlineAutoAssist.Tests;

public sealed class ConfigurableLiveComboTests
{
    private static LiveFrame Frame(long at, bool buff = false) => new(
        new GameSnapshot { Player = new PlayerState { Id = "local" }, Target = null },
        at, 51, "Player", null, buff, buff ? 7 : null, 0);

    [Fact]
    public void TargetlessCustomComboUsesImmediateFirstPairAnd200MsAfterSecond()
    {
        var clock = new SimulatedClock(0);
        var executor = new SimulatedActionExecutor(clock);
        var engine = new EarlyBuffPunishEngine(executor, clock, new EarlyPunishOptions
        { ComboKeys = new[] { "Z", "X", "C" }, FirstToSecondMs = 0, SecondToThirdMs = 200 });
        engine.Update(Frame(0, true), true, true, true);
        clock.Advance(50); engine.Update(Frame(50), true, true, true);
        Assert.Empty(executor.History);
        clock.Advance(50); var confirmed = Frame(100);
        engine.Update(confirmed, true, true, true);
        Assert.Equal(new[] { "Z", "X" }, executor.History.Select(h => h.Key));
        Assert.All(executor.History, h => Assert.Equal(100, h.AtMs));
        clock.Advance(199); engine.Update(confirmed, true, true, true);
        Assert.Equal(2, executor.History.Count);
        clock.Advance(1); engine.Update(confirmed, true, true, true);
        Assert.Equal(new[] { "Z", "X", "C" }, executor.History.Select(h => h.Key));
        Assert.Equal(300, executor.History[2].AtMs);
        Assert.Equal(EarlyPunishState.Completed, engine.State);
        engine.Update(confirmed, true, true, true);
        Assert.Equal(3, executor.History.Count);
    }

    [Theory]
    [InlineData("target")]
    [InlineData("none")]
    public void TargetChangesDoNotCancelPendingCombo(string change)
    {
        var clock = new SimulatedClock(0);
        var executor = new SimulatedActionExecutor(clock);
        var engine = new EarlyBuffPunishEngine(executor, clock);
        engine.Update(Frame(0, true), true, true, true);
        clock.Advance(50); engine.Update(Frame(50), true, true, true);
        clock.Advance(50); engine.Update(Frame(100), true, true, true);
        clock.Advance(120);
        var frame = Frame(220);
        if (change == "target") frame = frame with { Snapshot = frame.Snapshot with { Target = new TargetInfo { Id = "other" } } };
        engine.Update(frame, true, true, true);
        clock.Advance(120); engine.Update(Frame(340), true, true, true);
        Assert.Equal(3, executor.History.Count);
        Assert.Equal(EarlyPunishState.Completed, engine.State);
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(75, 200)]
    [InlineData(200, 25)]
    public void BothTransitionTimersAreIndependentAndMeasuredAfterKeyRelease(int first, int second)
    {
        var clock = new SimulatedClock(0);
        var executor = new HoldingExecutor(clock);
        var engine = new EarlyBuffPunishEngine(executor, clock, new EarlyPunishOptions
        { FirstToSecondMs = first, SecondToThirdMs = second });
        engine.Update(Frame(0, true), true, true, true);
        clock.Advance(50); engine.Update(Frame(50), true, true, true);
        clock.Advance(50); engine.Update(Frame(100), true, true, true);
        if (first > 0)
        {
            clock.Advance(first - 1); engine.Update(Frame(clock.NowMs), true, true, true);
            Assert.Single(executor.Downs);
            clock.Advance(1); engine.Update(Frame(clock.NowMs), true, true, true);
        }
        Assert.Equal(new long[] { 100, 140 + first }, executor.Downs);
        clock.Advance(second - 1); engine.Update(Frame(clock.NowMs), true, true, true);
        Assert.Equal(2, executor.Downs.Count);
        clock.Advance(1); engine.Update(Frame(clock.NowMs), true, true, true);
        Assert.Equal(new long[] { 100, 140 + first, 180 + first + second }, executor.Downs);
    }

    private sealed class HoldingExecutor(SimulatedClock clock) : IActionExecutor
    {
        public List<long> Downs { get; } = new();
        public ActionResult Execute(string actionId, string key)
        {
            Downs.Add(clock.NowMs);
            clock.Advance(40);
            return ActionResult.Ok("test key released");
        }
    }

    [Theory]
    [InlineData("gate")]
    [InlineData("permission")]
    [InlineData("buff")]
    [InlineData("stale")]
    public void PendingThirdKeyStillRechecksGuards(string guard)
    {
        var clock = new SimulatedClock(0);
        var executor = new SimulatedActionExecutor(clock);
        var engine = new EarlyBuffPunishEngine(executor, clock, new EarlyPunishOptions
        { FirstToSecondMs = 0, SecondToThirdMs = 200 });
        engine.Update(Frame(0, true), true, true, true);
        clock.Advance(50); engine.Update(Frame(50), true, true, true);
        clock.Advance(50); engine.Update(Frame(100), true, true, true);
        clock.Advance(guard == "stale" ? 501 : 200);
        engine.Update(guard == "stale" ? Frame(100) : Frame(clock.NowMs, guard == "buff"),
            true, guard != "permission", guard != "gate");
        Assert.Equal(2, executor.History.Count);
        Assert.Equal(EarlyPunishState.Cancelled, engine.State);
    }

    [Fact]
    public void MultipleAlternativeActivationKeysAreNormalizedAndPersisted()
    {
        var keys = LiveKeySettings.Validate(new[] { "a", "q", "s" }, new[] { "rightctrl", "E" });
        Assert.Equal(new[] { "A", "Q", "S", "RightCtrl", "E" }, keys);
        var path = Path.Combine(Path.GetTempPath(), $"ninautoassist-test-{Guid.NewGuid():N}.json");
        try
        {
            ConfigLoader.Save(path, new AutoAssistConfig { Live = new() { PunishKeys = keys.Take(3).ToArray(),
                TurnActivationKeys = keys.Skip(3).ToArray(), PunishFirstToSecondMs = 0, PunishSecondToThirdMs = 200 } });
            var saved = ConfigLoader.Load(path).Live;
            Assert.Equal(new[] { "RightCtrl", "E" }, saved.TurnActivationKeys);
            Assert.Equal(0, saved.PunishFirstToSecondMs);
            Assert.Equal(200, saved.PunishSecondToThirdMs);
            Assert.False(saved.AllowCounterAttackSequence);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("F12", "RightCtrl")]
    [InlineData("Up", "RightCtrl")]
    [InlineData("F", "RightCtrl")]
    [InlineData("RightCtrl", "E")]
    [InlineData("Q", "RightCtrl")]
    [InlineData("Z", "F12")]
    [InlineData("Z", "Left")]
    public void ReservedAndConflictingKeysAreRejected(string first, string activation) =>
        Assert.Throws<ArgumentException>(() => LiveKeySettings.Validate(new[] { first, "Q", "S" }, new[] { activation }));
}
