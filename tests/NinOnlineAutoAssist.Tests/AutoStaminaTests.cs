using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Simulation;

namespace NinOnlineAutoAssist.Tests;

public sealed class AutoStaminaTests
{
    [Fact]
    public void CustomThresholdsControlDispatchConfirmationAndHysteresis()
    {
        var clock = new SimulatedClock(0);
        var sent = new List<bool>();
        var engine = new AutoStaminaEngine(clock, desired => { sent.Add(desired); return ActionResult.Ok(); }, 30, 70);
        engine.Update(Frame(current: 30), true, true, false);
        Assert.Empty(sent);
        engine.Update(Frame(current: 29), true, true, false);
        Assert.Equal(new[] { false }, sent);
        clock.Advance(50); engine.Update(Frame(50, 29, false), true, true, false);
        clock.Advance(50); engine.Update(Frame(100, 70, false), true, true, false);
        Assert.Single(sent);
        clock.Advance(50); engine.Update(Frame(150, 71, false), true, true, false);
        Assert.Equal(new[] { false, true }, sent);
        clock.Advance(50); engine.Update(Frame(200, 50, true), true, true, false);
        Assert.True(engine.DesiredRunning);
    }

    [Fact]
    public void CustomThresholdsAreAlsoUsedForNewFrameDispatchRevalidation()
    {
        var engine = new AutoStaminaEngine(new SimulatedClock(0), _ => ActionResult.Ok(), 30, 70);
        engine.Update(Frame(current: 29), true, false, false);
        Assert.True(engine.NeedsToggle(Frame(current: 65)));
        Assert.False(engine.NeedsToggle(Frame(current: 71)));
        engine.Reset();
        engine.Update(Frame(current: 71, running: false), true, false, false);
        Assert.True(engine.NeedsToggle(Frame(current: 30, running: false)));
        Assert.False(engine.NeedsToggle(Frame(current: 29, running: false)));
    }

    private static LiveFrame Frame(long at = 0, int current = 100, bool running = true) => new(
        new GameSnapshot { Player = new PlayerState { Id = "local" } }, at, 51, "Local", null, false, null, 0)
        { Stamina = new(current, 100, running) };

    [Theory]
    [InlineData(19, true, false)]
    [InlineData(51, false, true)]
    public void StrictThresholdsRequestOnlyTheNecessaryToggle(int stamina, bool running, bool expected)
    {
        var sent = new List<bool>();
        var engine = new AutoStaminaEngine(new SimulatedClock(0), desired => { sent.Add(desired); return ActionResult.Ok(); });
        engine.Update(Frame(current: stamina, running: running), true, true, false);
        Assert.Equal(new[] { expected }, sent);
    }

    [Theory]
    [InlineData(20, true)]
    [InlineData(20, false)]
    [InlineData(50, true)]
    [InlineData(50, false)]
    [InlineData(35, true)]
    [InlineData(35, false)]
    [InlineData(19, false)]
    [InlineData(51, true)]
    public void InitialBandAndAlreadyCorrectStatesDoNotToggle(int stamina, bool running)
    {
        var count = 0;
        var engine = new AutoStaminaEngine(new SimulatedClock(0), _ => { count++; return ActionResult.Ok(); });
        engine.Update(Frame(current: stamina, running: running), true, true, false);
        Assert.Equal(0, count);
    }

    [Fact]
    public void OffDecisionIsRetainedUntilAboveHalfAndConfirmedByNewerSnapshot()
    {
        var clock = new SimulatedClock(0); var sent = new List<bool>();
        var engine = new AutoStaminaEngine(clock, desired => { sent.Add(desired); return ActionResult.Ok(); });
        engine.Update(Frame(current: 19), true, true, false);
        for (var i = 0; i < 10; i++) engine.Update(Frame(current: 19), true, true, false);
        Assert.Single(sent);
        clock.Advance(50); engine.Update(Frame(50, 20, false), true, true, false);
        clock.Advance(50); engine.Update(Frame(100, 50, false), true, true, false);
        Assert.False(engine.DesiredRunning); Assert.Single(sent);
        clock.Advance(50); engine.Update(Frame(150, 51, false), true, true, false);
        Assert.Equal(new[] { false, true }, sent);
        clock.Advance(50); engine.Update(Frame(200, 30, true), true, true, false);
        Assert.True(engine.DesiredRunning); Assert.Equal(2, sent.Count);
    }

    [Fact]
    public void ConfirmationMustBeNewerAndTimeoutNeverSpamsAnotherToggle()
    {
        var clock = new SimulatedClock(0); var count = 0;
        var engine = new AutoStaminaEngine(clock, _ => { count++; return ActionResult.Ok(); });
        engine.Update(Frame(current: 19), true, true, false);
        engine.Update(Frame(current: 19, running: false), true, true, false);
        Assert.Contains("aguardando confirmação", engine.Status);
        clock.Advance(1500); engine.Update(Frame(1500, 19), true, true, false);
        clock.Advance(5000); engine.Update(Frame(6500, 19), true, true, false);
        Assert.Equal(1, count); Assert.Contains("sem confirmação", engine.Status);
        engine.Update(Frame(6500, 19), false, true, false);
        engine.Update(Frame(6500, 19), true, true, false);
        Assert.Equal(2, count);
    }

    [Theory]
    [InlineData("off")]
    [InlineData("gate")]
    [InlineData("priority")]
    [InlineData("unknown")]
    [InlineData("invalid")]
    [InlineData("stale")]
    [InlineData("future")]
    public void MissingPermissionOrReliableStateCannotDispatch(string reason)
    {
        var count = 0;
        var engine = new AutoStaminaEngine(new SimulatedClock(0), _ => { count++; return ActionResult.Ok(); });
        var frame = Frame(reason == "stale" ? -501 : reason == "future" ? 1 : 0, 19);
        if (reason == "unknown") frame = frame with { Stamina = null };
        if (reason == "invalid") frame = frame with { Stamina = new(101, 100, true) };
        engine.Update(frame, reason != "off", reason != "gate", reason == "priority");
        Assert.Equal(0, count);
    }

    [Fact]
    public void NewFrameThatCrossedOppositeThresholdInvalidatesDispatchIntent()
    {
        var engine = new AutoStaminaEngine(new SimulatedClock(0), _ => ActionResult.Ok());
        engine.Update(Frame(current: 19), true, false, false);
        Assert.True(engine.NeedsToggle(Frame(current: 19)));
        Assert.False(engine.NeedsToggle(Frame(current: 51)));
    }

    [Fact]
    public void RefusedInputLatchesUntilExplicitRearm()
    {
        var count = 0;
        var engine = new AutoStaminaEngine(new SimulatedClock(0), _ => { count++; return ActionResult.Rejected("held manually"); });
        for (var i = 0; i < 10; i++) engine.Update(Frame(current: 19), true, true, false);
        Assert.Equal(1, count);
    }

    [Theory]
    [InlineData("off")]
    [InlineData("focus")]
    [InlineData("emergency")]
    [InlineData("text")]
    [InlineData("dead")]
    [InlineData("stale")]
    public void SafetyGateIsIndependentOfTargetsButPreservesCommonGuards(string reason)
    {
        var frame = Frame();
        if (reason == "dead") frame = frame with { Snapshot = frame.Snapshot with { Player = frame.Snapshot.Player with { IsAlive = false } } };
        Assert.True(LiveSafetyGate.CheckStamina(Frame(), 0, true, true, false, false).Allowed);
        Assert.False(LiveSafetyGate.CheckStamina(frame, reason == "stale" ? 501 : 0,
            reason != "off", reason != "focus", reason == "emergency", reason == "text").Allowed);
    }
}
