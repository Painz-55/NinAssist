using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Simulation;

namespace NinOnlineAutoAssist.Tests;

public sealed class TaiSubstitutionTests
{
    private const string TaiBuff = "Speed Mirage Technique";
    private static ObservedBuff Tai(int? duration = 5) => new(TaiBuff, duration);
    private static ObservedBuff Normal(int? duration = 7) => new(SubstitutionBuffs.Normal, duration);
    private static ObservedBuff Water(int? duration = 5) => new(SubstitutionBuffs.Water, duration);
    private static LiveFrame Frame(long time, params ObservedBuff[] buffs) => new(
        new GameSnapshot { Player = new PlayerState { Id = "local:Skyrend" }, Target = null,
            ActiveBuffs = buffs.Select(b => b.Name).ToArray() }, time, 51, "Skyrend", null,
        buffs.Any(b => SubstitutionBuffs.IsKnown(b.Name)), null, 0) { ObservedActiveBuffs = buffs };

    private sealed class Rig
    {
        public SimulatedClock Clock { get; } = new(0);
        public SimulatedActionExecutor Executor { get; }
        public EarlyBuffPunishEngine Engine { get; }
        public Rig(long delay = 0)
        {
            Executor = new SimulatedActionExecutor(Clock);
            Engine = new EarlyBuffPunishEngine(Executor, Clock, new EarlyPunishOptions
            { TriggerDelayMs = delay, FirstToSecondMs = 0, SecondToThirdMs = 200 });
        }
        public void At(long time, params ObservedBuff[] buffs)
        {
            Clock.Advance(time - Clock.NowMs);
            Engine.Update(Frame(time, buffs), true, true, true);
        }
    }

    [Fact]
    public void TaiAloneTriggersTargetlessComboWithConfiguredDelayAndNoRepeat()
    {
        var rig = new Rig(150);
        rig.At(0, Tai()); rig.At(100); rig.At(200);
        Assert.Empty(rig.Executor.History);
        rig.At(349);
        Assert.Empty(rig.Executor.History);
        rig.At(350);
        Assert.Equal(new[] { "A", "Q" }, rig.Executor.History.Select(h => h.Key));
        Assert.Equal(TaiBuff, Assert.Single(rig.Engine.TriggerSources));
        rig.At(549);
        Assert.Equal(2, rig.Executor.History.Count);
        rig.At(550); rig.At(700); rig.At(900);
        Assert.Equal(new[] { "A", "Q", "S" }, rig.Executor.History.Select(h => h.Key));
    }

    [Theory]
    [InlineData("tai")]
    [InlineData("normal")]
    [InlineData("water")]
    public void EachBuffCanTriggerIndependentlyWhileTheOtherTwoRemainActive(string removed)
    {
        var rig = new Rig(); rig.At(0, Tai(), Normal(), Water());
        var remaining = removed switch
        {
            "tai" => new[] { Normal(), Water() },
            "normal" => new[] { Tai(), Water() },
            _ => new[] { Tai(), Normal() }
        };
        rig.At(100, remaining); rig.At(200, remaining); rig.At(400, remaining);
        Assert.Equal(new[] { "A", "Q", "S" }, rig.Executor.History.Select(h => h.Key));
        var frame = Frame(400, remaining);
        Assert.True(frame.SubstitutionActive);
        Assert.True(rig.Engine.TriggerBuffsRemainAbsent(frame));
    }

    [Fact]
    public void ThreeSimultaneousEarlyRemovalsProduceOnlyOneCombo()
    {
        var rig = new Rig(); rig.At(0, Tai(), Normal(), Water());
        rig.At(100); rig.At(200);
        Assert.Equal(3, rig.Engine.TriggerSources.Count);
        rig.At(400); rig.At(600); rig.At(800);
        Assert.Equal(3, rig.Executor.History.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(null)]
    public void TaiNaturalOrUnknownExpiryCannotBorrowAnotherBuffCountdown(int? duration)
    {
        var rig = new Rig(); rig.At(0, Tai(duration), Normal(), Water());
        rig.At(100, Normal(), Water()); rig.At(200, Normal(), Water()); rig.At(400, Normal(), Water());
        Assert.Empty(rig.Executor.History);
    }

    [Fact]
    public void ReappearingTaiCancelsItsPendingCombo()
    {
        var rig = new Rig(); rig.At(0, Tai(), Normal());
        rig.At(100, Normal()); rig.At(200, Normal()); rig.At(300, Tai(), Normal());
        Assert.Equal(EarlyPunishState.Cancelled, rig.Engine.State);
        Assert.Equal(2, rig.Executor.History.Count);
    }

    [Fact]
    public void CapturedTaiCountdownCanExpireBeforeNormalWithoutTriggering()
    {
        var rig = new Rig();
        for (var time = 0; time <= 5500; time += 250)
        {
            var normal = Normal(Math.Max(0, 6 - time / 1000));
            var brokenBones = new ObservedBuff("Broken Bones", 220 - time / 1000);
            if (time < 5500) rig.At(time, normal, Tai(Math.Max(0, 5 - time / 1000)), brokenBones);
            else rig.At(time, normal, brokenBones);
        }
        Assert.Empty(rig.Executor.History);
        Assert.Equal(BuffPresence.Active, LiveBuffObservation.FromFrame(Frame(5500, Normal(1)), 5500).Presence);
    }

    [Fact]
    public void BrokenBonesDoesNotMaskTaiRemovalOrAppearAsAnActiveSub()
    {
        var rig = new Rig();
        var brokenBones = new ObservedBuff("Broken Bones", 220);
        rig.At(0, Tai(), brokenBones); rig.At(100, brokenBones); rig.At(200, brokenBones); rig.At(400, brokenBones);
        Assert.Equal(3, rig.Executor.History.Count);
        Assert.Equal(TaiBuff, Assert.Single(rig.Engine.TriggerSources));
        Assert.Equal(BuffPresence.Inactive, LiveBuffObservation.FromFrame(Frame(400, brokenBones), 400).Presence);
        Assert.False(Frame(400, brokenBones).SubstitutionActive);
    }

    [Fact]
    public void TransientTaiAbsenceNeverBecomesConfirmedRemoval()
    {
        var rig = new Rig(); rig.At(0, Tai(), Normal());
        rig.At(100, Normal()); rig.At(200, Tai(), Normal()); rig.At(300, Tai(), Normal());
        Assert.Empty(rig.Executor.History);
    }

    [Fact]
    public void TaiDisplaysItsOwnNameAndDuration()
    {
        var observation = LiveBuffObservation.FromFrame(Frame(0, Tai(3), Normal(6)), 0);
        Assert.Equal(2, observation.ActiveSubs.Count);
        Assert.Contains(observation.ActiveSubs, b => b.Name == TaiBuff && b.RemainingSeconds == 3);
        Assert.Equal("Tai", SubstitutionBuffs.Label(TaiBuff));
    }

    [Fact]
    public void TaiKeepsTurnWindowOpenAfterNormalDisappearsAndThenGetsOneSecondTail()
    {
        var window = new SubstitutionTurnWindow();
        window.Update(Frame(0, Tai(), Normal()), 0, true);
        window.Update(Frame(100, Tai()), 100, true);
        Assert.True(window.IsOpen(Frame(100, Tai()), 100));
        window.Update(Frame(500, Tai()), 500, true);
        window.Update(Frame(600), 600, true);
        window.Update(Frame(1000), 1000, true);
        window.Update(Frame(1400), 1400, true);
        Assert.True(window.IsOpen(Frame(1400), 1599));
        Assert.False(window.IsOpen(Frame(1400), 1600));
    }
}
