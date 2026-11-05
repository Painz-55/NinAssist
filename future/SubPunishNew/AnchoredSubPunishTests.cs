using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Simulation;

namespace NinOnlineAutoAssist.Tests;

// Archived tests for a future separate experiment; excluded from the active solution.
public sealed class AnchoredSubPunishTests
{
    private static LiveFrame Frame(long at, double x, int? seconds, params string[] names) => new(
        new GameSnapshot { Player = new PlayerState { Id = "local", PositionX = x, PositionY = 5 } },
        at, 51, "Local", null, names.Length > 0, seconds, 0)
        { ObservedActiveBuffs = names.Select(n => new ObservedBuff(n, seconds)).ToArray() };
    private static EarlyPunishOptions Options => new() { FirstToSecondMs = 10, SecondToThirdMs = 100 };

    [Theory]
    [InlineData(SubstitutionBuffs.Normal)]
    [InlineData(SubstitutionBuffs.Water)]
    [InlineData(SubstitutionBuffs.Tai)]
    public void CorrelatedJumpAndFirstEarlyAbsenceFireWithoutSecondAbsentSample(string source)
    {
        var clock = new SimulatedClock(0); var sent = new List<string>();
        var engine = new AnchoredSubPunishEngine(new Executor(k => { sent.Add(k); return ActionResult.Ok(); }), clock, Options);
        engine.Update(Frame(0, 10, 5, source), true, true, true);
        clock.Advance(40); engine.Update(Frame(40, 12, null), true, true, true);
        Assert.Equal(new[] { "A" }, sent); Assert.Equal(EarlyPunishState.Executing, engine.State);
        engine.Update(Frame(40, 12, null), true, true, true); Assert.Single(sent);
        clock.Advance(10); engine.Update(Frame(50, 12, null), true, true, true);
        clock.Advance(100); engine.Update(Frame(150, 12, null), true, true, true);
        Assert.Equal(new[] { "A", "Q", "S" }, sent);
        Assert.Equal(EarlyPunishState.Completed, engine.State);
    }

    [Fact]
    public void TargetlessComboContinuesAcrossModerateReadGapButNotF12()
    {
        var clock = new SimulatedClock(0); var sent = new List<string>();
        var engine = new AnchoredSubPunishEngine(new Executor(k => { sent.Add(k); return ActionResult.Ok(); }), clock, Options);
        engine.Update(Frame(0, 10, 5, SubstitutionBuffs.Normal), true, true, true);
        clock.Advance(40); engine.Update(Frame(40, 12, null), true, true, true);
        clock.Advance(120); engine.Update(Frame(160, 12, null), true, true, true);
        clock.Advance(100); engine.Update(Frame(260, 12, null), true, true, true);
        Assert.Equal(["A", "Q", "S"], sent);
        engine.Reset(); sent.Clear();
        clock.Advance(10); engine.Update(Frame(270, 10, 5, SubstitutionBuffs.Normal), true, true, true);
        clock.Advance(40); engine.Update(Frame(310, 12, null), true, true, true);
        clock.Advance(10); engine.Update(Frame(320, 12, null), true, true, false, "Emergency stop");
        clock.Advance(150); engine.Update(Frame(470, 12, null), true, true, true);
        Assert.Equal(["A"], sent);
    }

    [Fact]
    public void GapWithBuffRearmsButNeverTreatsMissingSamplesAsEvidence()
    {
        var clock = new SimulatedClock(0); var sent = new List<string>();
        var engine = new AnchoredSubPunishEngine(new Executor(k => { sent.Add(k); return ActionResult.Ok(); }), clock, Options);
        engine.Update(Frame(0, 10, 5, SubstitutionBuffs.Normal), true, true, true);
        clock.Advance(140); engine.Update(Frame(140, 12, 5, SubstitutionBuffs.Normal), true, true, true);
        Assert.Empty(sent);
        clock.Advance(40); engine.Update(Frame(180, 14, null), true, true, true);
        Assert.Equal(["A"], sent); // new adjacent jump + early removal, not the lost 140ms interval
    }

    [Fact]
    public void DisabledMiddleSlotDoesNotSendInput()
    {
        var clock = new SimulatedClock(0); var sent = new List<string>();
        var engine = new AnchoredSubPunishEngine(new Executor(k => { sent.Add(k); return ActionResult.Ok(); }), clock,
            Options with { ComboKeys = ["A", "null", "S"] });
        engine.Update(Frame(0, 10, 5, SubstitutionBuffs.Normal), true, true, true);
        clock.Advance(40); engine.Update(Frame(40, 12, null), true, true, true);
        clock.Advance(10); engine.Update(Frame(50, 12, null), true, true, true);
        clock.Advance(100); engine.Update(Frame(150, 12, null), true, true, true);
        Assert.Equal(["A", "S"], sent);
    }


    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ArrivalOrderOfJumpAndRemovalCanDifferWithinTheCorrelationWindow(bool jumpFirst)
    {
        var clock = new SimulatedClock(0); var sent = 0;
        var engine = new AnchoredSubPunishEngine(new Executor(_ => { sent++; return ActionResult.Ok(); }), clock, Options);
        engine.Update(Frame(0, 10, 5, SubstitutionBuffs.Normal), true, true, true);
        clock.Advance(30); engine.Update(jumpFirst ? Frame(30, 12, 5, SubstitutionBuffs.Normal) : Frame(30, 10, null), true, true, true);
        Assert.Equal(0, sent);
        clock.Advance(30); engine.Update(Frame(60, 12, null), true, true, true);
        Assert.Equal(1, sent);
    }

    [Theory]
    [InlineData("removal-only")]
    [InlineData("walk")]
    [InlineData("dash-with-buff")]
    [InlineData("no-sub")]
    [InlineData("natural")]
    [InlineData("duration-unknown")]
    [InlineData("large-gap")]
    [InlineData("map")]
    [InlineData("identity")]
    [InlineData("stale")]
    [InlineData("window-expired")]
    public void SingleUncorrelatedOrUnreliableSignalsNeverFire(string reason)
    {
        var clock = new SimulatedClock(0); var sent = 0;
        var engine = new AnchoredSubPunishEngine(new Executor(_ => { sent++; return ActionResult.Ok(); }), clock, Options);
        var initial = reason == "no-sub" ? Frame(0, 10, null) : Frame(0, 10,
            reason == "natural" ? 1 : reason == "duration-unknown" ? null : 5, SubstitutionBuffs.Normal);
        engine.Update(initial, true, true, true);
        var at = reason == "large-gap" ? 140 : 40;
        clock.Advance(at);
        var after = Frame(at, reason is "removal-only" or "walk" or "window-expired" ? 10.2 : 12, null);
        if (reason == "dash-with-buff") after = Frame(at, 15, 5, SubstitutionBuffs.Normal);
        if (reason == "map") after = after with { Map = 52 };
        if (reason == "identity") after = after with { Snapshot = after.Snapshot with { Player = after.Snapshot.Player with { Id = "other" } } };
        if (reason == "stale") clock.Advance(501);
        engine.Update(after, true, true, true);
        if (reason == "window-expired")
        {
            clock.Advance(80); engine.Update(Frame(120, 10.2, null), true, true, true);
            clock.Advance(60); engine.Update(Frame(180, 12.2, null), true, true, true);
        }
        Assert.Equal(0, sent);
    }

    [Fact]
    public void FirstAbsenceWithNoJumpNeverSilentlyFallsBackToLegacy()
    {
        var clock = new SimulatedClock(0); var sent = 0;
        var engine = new AnchoredSubPunishEngine(new Executor(_ => { sent++; return ActionResult.Ok(); }), clock, Options);
        engine.Update(Frame(0, 10, 5, SubstitutionBuffs.Normal), true, true, true);
        for (var i = 1; i <= 20; i++)
        { clock.Advance(20); engine.Update(Frame(i * 20, 10, null), true, true, true); }
        Assert.Equal(0, sent);
        Assert.Contains("nenhuma tecla", engine.Status);
    }

    [Fact]
    public void SimultaneousSubsCoalesceAndBuffReappearanceCancelsTheRest()
    {
        var clock = new SimulatedClock(0); var sent = 0;
        var engine = new AnchoredSubPunishEngine(new Executor(_ => { sent++; return ActionResult.Ok(); }), clock, Options);
        engine.Update(Frame(0, 10, 5, SubstitutionBuffs.Normal, SubstitutionBuffs.Water), true, true, true);
        clock.Advance(40); engine.Update(Frame(40, 12, null), true, true, true); Assert.Equal(1, sent);
        clock.Advance(20); engine.Update(Frame(60, 12, 5, SubstitutionBuffs.Water), true, true, true);
        Assert.Equal(EarlyPunishState.Cancelled, engine.State); Assert.Equal(1, sent);
    }

    [Theory]
    [InlineData("off")]
    [InlineData("authorization")]
    [InlineData("safety")]
    public void RevocationCannotReleaseAnyFurtherComboKey(string reason)
    {
        var clock = new SimulatedClock(0); var sent = 0;
        var engine = new AnchoredSubPunishEngine(new Executor(_ => { sent++; return ActionResult.Ok(); }), clock, Options);
        engine.Update(Frame(0, 10, 5, SubstitutionBuffs.Normal), true, true, true);
        clock.Advance(40); engine.Update(Frame(40, 12, null), true, true, true);
        clock.Advance(20); engine.Update(Frame(60, 12, null), reason != "off", reason != "authorization", reason != "safety");
        Assert.Equal(1, sent); Assert.Equal(EarlyPunishState.Cancelled, engine.State);
    }

    [Fact]
    public void DebugDelayAndDeferredFacingPreserveTheAnchorWithoutDuplicateInputs()
    {
        var clock = new SimulatedClock(0); var sent = 0; var ready = false;
        var engine = new AnchoredSubPunishEngine(new Executor(_ =>
        {
            if (!ready) return ActionResult.Deferred("new aligned snapshot required");
            sent++; return ActionResult.Ok();
        }), clock, Options with { TriggerDelayMs = 50 });
        engine.Update(Frame(0, 10, 5, SubstitutionBuffs.Normal), true, true, true);
        clock.Advance(40); engine.Update(Frame(40, 12, null), true, true, true); Assert.Equal(0, sent);
        clock.Advance(50); engine.Update(Frame(90, 12, null), true, true, true); Assert.Equal(0, sent);
        ready = true; clock.Advance(20); engine.Update(Frame(110, 12, null), true, true, true); Assert.Equal(1, sent);
    }

    [Fact]
    public void FactoryKeepsLegacyAsDefaultAndNeverRunsBothEngines()
    {
        var clock = new SimulatedClock(0); var executor = new Executor(_ => ActionResult.Ok());
        Assert.Equal(LivePunishMode.Legacy, new AutoAssistConfig.LiveSection().PunishMode);
        Assert.IsType<EarlyBuffPunishEngine>(LivePunishFactory.Create(LivePunishMode.Legacy, executor, clock, Options));
        Assert.IsType<AnchoredSubPunishEngine>(LivePunishFactory.Create(LivePunishMode.AnchoredExperimental, executor, clock, Options));
        Assert.Throws<ArgumentException>(() => LivePunishFactory.Create((LivePunishMode)99, executor, clock, Options));
    }

    [Fact]
    public void ModeRoundTripPreservesLegacyDefaultAndNeverGrantsPermission()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nin-sub-mode-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{}");
            var original = ConfigLoader.Load(path);
            Assert.Equal(LivePunishMode.Legacy, original.Live.PunishMode);
            ConfigLoader.Save(path, original with { Live = original.Live with { PunishMode = LivePunishMode.AnchoredExperimental } });
            var updated = ConfigLoader.Load(path);
            Assert.Equal(LivePunishMode.AnchoredExperimental, updated.Live.PunishMode);
            Assert.False(updated.Live.AllowCounterAttackSequence);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RemainingWaterDoesNotCancelNormalTriggerAndLateRemovalNeverQueuesAnotherCombo()
    {
        var clock = new SimulatedClock(0); var sent = new List<string>();
        var engine = new AnchoredSubPunishEngine(new Executor(k => { sent.Add(k); return ActionResult.Ok(); }), clock, Options);
        engine.Update(Frame(0, 10, 5, SubstitutionBuffs.Normal, SubstitutionBuffs.Water), true, true, true);
        clock.Advance(40); engine.Update(Frame(40, 12, 5, SubstitutionBuffs.Water), true, true, true);
        Assert.Equal(new[] { "A" }, sent);
        Assert.True(engine.TriggerBuffsRemainAbsent(Frame(40, 12, 5, SubstitutionBuffs.Water)));
        clock.Advance(20); engine.Update(Frame(60, 12, null), true, true, true);
        clock.Advance(100); engine.Update(Frame(160, 12, null), true, true, true);
        for (var at = 180; at <= 400; at += 20)
        { clock.Advance(at - clock.NowMs); engine.Update(Frame(at, 12, null), true, true, true); }
        Assert.Equal(new[] { "A", "Q", "S" }, sent);
    }

    [Fact]
    public void DeferredInputTimesOutWithoutReplayingTheConsumedAnchor()
    {
        var clock = new SimulatedClock(0); var attempts = 0;
        var engine = new AnchoredSubPunishEngine(new Executor(_ => { attempts++; return ActionResult.Deferred("held manually"); }), clock, Options);
        engine.Update(Frame(0, 10, 5, SubstitutionBuffs.Normal), true, true, true);
        clock.Advance(40); engine.Update(Frame(40, 12, null), true, true, true);
        for (var at = 90; at <= 1640; at += 50)
        { clock.Advance(at - clock.NowMs); engine.Update(Frame(at, 12, null), true, true, true); }
        Assert.Equal(EarlyPunishState.Cancelled, engine.State);
        var stopped = attempts;
        clock.Advance(50); engine.Update(Frame(clock.NowMs, 12, null), true, true, true);
        Assert.Equal(stopped, attempts);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(101, 2)]
    [InlineData(50, 0.6)]
    [InlineData(100, 1.5)]
    [InlineData(50, 9)]
    public void ImplausibleOrSlowDisplacementsAreNotTeleportAnchors(long dt, double distance)
    {
        Assert.Null(SubTeleportCorrelation.Observe(Frame(0, 10, 5, SubstitutionBuffs.Normal),
            Frame(dt, 10 + distance, null)));
    }

    private sealed class Executor(Func<string, ActionResult> execute) : IActionExecutor
    {
        public ActionResult Execute(string actionId, string key) => execute(key);
    }
}
