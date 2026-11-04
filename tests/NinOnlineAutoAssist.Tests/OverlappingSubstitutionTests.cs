using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Simulation;

namespace NinOnlineAutoAssist.Tests;

public sealed class OverlappingSubstitutionTests
{
    private static LiveFrame Frame(long time, params ObservedBuff[] buffs) => new(
        new GameSnapshot { Player = new PlayerState { Id = "local" }, Target = null,
            ActiveBuffs = buffs.Select(b => b.Name).ToArray() }, time, 51, "Player", null,
        buffs.Any(b => SubstitutionBuffs.IsKnown(b.Name)), null, 0) { ObservedActiveBuffs = buffs };
    private static ObservedBuff Normal(int? duration = 7) => new(SubstitutionBuffs.Normal, duration);
    private static ObservedBuff Water(int? duration = 5) => new(SubstitutionBuffs.Water, duration);

    private sealed class Rig
    {
        public SimulatedClock Clock { get; } = new(0);
        public SimulatedActionExecutor Executor { get; }
        public EarlyBuffPunishEngine Engine { get; }
        public Rig()
        {
            Executor = new SimulatedActionExecutor(Clock);
            Engine = new EarlyBuffPunishEngine(Executor, Clock, new EarlyPunishOptions
            { FirstToSecondMs = 0, SecondToThirdMs = 200 });
        }
        public LiveFrame At(long time, params ObservedBuff[] buffs)
        {
            Clock.Advance(time - Clock.NowMs);
            var frame = Frame(time, buffs);
            Engine.Update(frame, true, true, true);
            return frame;
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EitherSubCanTriggerWhileTheOtherRemainsActive(bool removeWater)
    {
        var rig = new Rig();
        rig.At(0, Normal(), Water());
        var remaining = removeWater ? Normal() : Water();
        rig.At(100, remaining);
        Assert.Empty(rig.Executor.History);
        var frame = rig.At(200, remaining);
        Assert.True(frame.SubstitutionActive);
        Assert.True(rig.Engine.TriggerBuffsRemainAbsent(frame));
        Assert.Equal(removeWater ? SubstitutionBuffs.Water : SubstitutionBuffs.Normal, Assert.Single(rig.Engine.TriggerSources));
        Assert.Equal(2, rig.Executor.History.Count);
        rig.At(400, remaining);
        Assert.Equal(new[] { "A", "Q", "S" }, rig.Executor.History.Select(h => h.Key));
        rig.At(500, remaining); rig.At(600, remaining);
        Assert.Equal(3, rig.Executor.History.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NaturalExpiryOfOneSubDoesNotBorrowTheOtherCountdown(bool expireNormal)
    {
        var rig = new Rig();
        rig.At(0, expireNormal ? Normal(0) : Normal(), expireNormal ? Water() : Water(0));
        var remaining = expireNormal ? Water() : Normal();
        rig.At(100, remaining); rig.At(200, remaining); rig.At(400, remaining);
        Assert.Empty(rig.Executor.History);
    }

    [Fact]
    public void ActualCapturedNormalZeroPlusWaterFiveDoesNotProduceFalseCombo()
    {
        var rig = new Rig();
        rig.At(0, Normal(0), Water(5)); rig.At(100, Normal(0), Water(4));
        rig.At(200, Water(5)); // observed increase must not grant a fresh countdown
        rig.At(300, Water(4)); rig.At(400, Water(4));
        Assert.Empty(rig.Executor.History);
    }

    [Fact]
    public void SimultaneousEarlyRemovalsProduceOnlyOneCombo()
    {
        var rig = new Rig(); rig.At(0, Normal(), Water());
        rig.At(100); rig.At(200);
        Assert.Equal(2, rig.Engine.TriggerSources.Count);
        rig.At(400); rig.At(600); rig.At(800);
        Assert.Equal(new[] { "A", "Q", "S" }, rig.Executor.History.Select(h => h.Key));
    }

    [Fact]
    public void OtherRemovalDuringComboIsConsumedWithoutQueuingASecondCombo()
    {
        var rig = new Rig(); rig.At(0, Normal(), Water());
        rig.At(100, Normal()); rig.At(200, Normal());
        rig.At(250); rig.At(300);
        rig.At(400); rig.At(600); rig.At(800);
        Assert.Equal(3, rig.Executor.History.Count);
    }

    [Fact]
    public void LaterSeparateRemovalOfRemainingBuffCanTriggerItsOwnCombo()
    {
        var rig = new Rig(); rig.At(0, Normal(), Water());
        rig.At(100, Normal()); rig.At(200, Normal()); rig.At(400, Normal());
        rig.At(500, Normal()); rig.At(600); rig.At(700); rig.At(900);
        Assert.Equal(new[] { "A", "Q", "S", "A", "Q", "S" }, rig.Executor.History.Select(h => h.Key));
    }

    [Fact]
    public void ReappearanceOfTriggeringSourceCancelsEvenIfOtherSubNeverLeft()
    {
        var rig = new Rig(); rig.At(0, Normal(), Water());
        rig.At(100, Normal()); rig.At(200, Normal()); rig.At(300, Normal(), Water());
        Assert.Equal(EarlyPunishState.Cancelled, rig.Engine.State);
        Assert.Equal(2, rig.Executor.History.Count);
    }

    [Fact]
    public void StartingAnotherBuffDoesNotCancelTheCurrentCombo()
    {
        var rig = new Rig(); rig.At(0, Water()); rig.At(100); rig.At(200);
        rig.At(300, Normal()); rig.At(400, Normal());
        Assert.Equal(3, rig.Executor.History.Count);
    }

    [Fact]
    public void UnknownDurationCannotBorrowKnownDurationFromOtherBuff()
    {
        var rig = new Rig(); rig.At(0, Normal(), Water(null));
        rig.At(100, Normal()); rig.At(200, Normal()); rig.At(400, Normal());
        Assert.Empty(rig.Executor.History);
    }

    [Fact]
    public void TransientAbsenceDoesNotConfirmAcrossDifferentBuffs()
    {
        var rig = new Rig(); rig.At(0, Normal(), Water());
        rig.At(100, Normal()); rig.At(200, Water());
        Assert.Empty(rig.Executor.History);
        rig.At(300, Normal(), Water());
        Assert.Empty(rig.Executor.History);
    }

    [Fact]
    public void TurnWindowRemainsOpenUntilLastSubDisappearsThenOnlyOneSecond()
    {
        var window = new SubstitutionTurnWindow();
        void At(long at, params ObservedBuff[] buffs) => window.Update(Frame(at, buffs), at, true);
        At(0, Normal(), Water()); At(100, Water()); At(500, Water()); At(900, Water());
        Assert.True(window.IsOpen(Frame(900, Water()), 900));
        At(1000); At(1400); At(1800);
        Assert.True(window.IsOpen(Frame(1800), 1999));
        Assert.False(window.IsOpen(Frame(1800), 2000));
    }

    [Fact]
    public void BuffDisplayShowsBothIndependentDurations()
    {
        var observation = LiveBuffObservation.FromFrame(Frame(0, Normal(2), Water(5)), 0);
        Assert.Equal(BuffPresence.Active, observation.Presence);
        Assert.Equal(new int?[] { 2, 5 }, observation.ActiveSubs.Select(b => b.RemainingSeconds));
    }

    [Fact]
    public void UnvalidatedEvasionCandidateAndUnrelatedNamesDoNotEnableAutomation()
    {
        Assert.False(SubstitutionBuffs.IsKnown("Evasion Technique"));
        Assert.False(SubstitutionBuffs.IsKnown("Broken Bones"));
        Assert.False(SubstitutionBuffs.IsKnown("Water Substitution Technique cooldown"));
        var rig = new Rig(); rig.At(0, new ObservedBuff("Evasion Technique", 7));
        rig.At(100); rig.At(200);
        Assert.Empty(rig.Executor.History);
    }

    [Fact]
    public void ComboKeysCanAlsoBeTurnActivationKeys()
    {
        var all = LiveKeySettings.Validate(new[] { "A", "Q", "S" }, new[] { "a", "q", "s", "RightCtrl" });
        Assert.Equal(new[] { "A", "Q", "S", "A", "Q", "S", "RightCtrl" }, all);
        Assert.Throws<ArgumentException>(() => LiveKeySettings.Validate(new[] { "A", "Q", "S" }, new[] { "A", "a" }));
    }

    private sealed class DeferredExecutor : IActionExecutor
    {
        public bool Held { get; set; } = true;
        public List<string> Sent { get; } = new();
        public ActionResult Execute(string actionId, string boundKey)
        {
            if (boundKey == "Q" && Held) return ActionResult.Deferred("manual hold");
            Sent.Add(boundKey);
            return ActionResult.Ok();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedHeldKeyWaitsWithoutDuplicatingEarlierKeysAndHonorsTimeout(bool timeout)
    {
        var clock = new SimulatedClock(0);
        var executor = new DeferredExecutor();
        var engine = new EarlyBuffPunishEngine(executor, clock, new EarlyPunishOptions { FirstToSecondMs = 0, SecondToThirdMs = 200 });
        engine.Update(Frame(0, Water()), true, true, true);
        clock.Advance(50); engine.Update(Frame(50), true, true, true);
        clock.Advance(50); engine.Update(Frame(100), true, true, true);
        Assert.Equal(new[] { "A" }, executor.Sent);
        for (var at = 200; at <= (timeout ? 1700 : 300); at += 100)
        {
            clock.Advance(100); engine.Update(Frame(at), true, true, true);
        }
        if (timeout)
        {
            Assert.Equal(EarlyPunishState.Cancelled, engine.State);
            Assert.Equal(new[] { "A" }, executor.Sent);
        }
        else
        {
            executor.Held = false;
            clock.Advance(100); engine.Update(Frame(400), true, true, true);
            clock.Advance(200); engine.Update(Frame(600), true, true, true);
            Assert.Equal(new[] { "A", "Q", "S" }, executor.Sent);
        }
    }
}
