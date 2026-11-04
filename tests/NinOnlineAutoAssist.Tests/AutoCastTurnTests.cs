using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Simulation;

namespace NinOnlineAutoAssist.Tests;

public sealed class AutoCastTurnTests
{
    private static CastObservation Idle => new(false, 0, 0, 0, 0);
    private static CastObservation Active(long remaining, int spell = 42, long start = 1000, long total = 1000) => new(true, spell, start, total, remaining);
    private static LiveFrame Frame(long at, CastObservation? cast, Direction facing = Direction.Down,
        string target = "target", double targetX = 0, double targetY = -5) => new(new GameSnapshot
        {
            Player = new PlayerState { Id = "local", Facing = facing },
            Target = new TargetInfo { Id = target, IsTargeted = true, PositionX = targetX, PositionY = targetY }
        }, at, 51, "Local", "Target", false, null, 0) { Cast = cast };

    [Fact]
    public void AnySpellWithObservedCastIsMonitoredInFinalWindowWithoutActivationKey()
    {
        var clock = new SimulatedClock(0); var sent = new List<Direction>();
        var engine = new AutoCastTurnEngine(clock, new(), d => { sent.Add(d); return TurnResult.Ok(); });
        engine.Update(Frame(0, Idle), true, false);
        clock.Advance(100); engine.Update(Frame(100, Active(1000, spell: 777)), true, false);
        for (var at = 200; at <= 1000; at += 100)
        { clock.Advance(100); engine.Update(Frame(at, Active(1100 - at, spell: 777)), true, false); }
        Assert.Equal(new[] { Direction.Up }, sent);
        clock.Advance(50); engine.Update(Frame(1050, Active(50, spell: 777), Direction.Up), true, false);
        engine.Update(Frame(1050, Active(50, spell: 777), Direction.Up), true, false);
        Assert.Single(sent); Assert.Null(engine.Dispatching);
        clock.Advance(50); engine.Update(Frame(1100, Idle), true, false);
        clock.Advance(50); engine.Update(Frame(1150, Active(400, spell: 333, start: 2000, total: 400)), true, false);
        for (var at = 1250; at <= 1450; at += 100)
        { clock.Advance(100); engine.Update(Frame(at, Active(1550 - at, spell: 333, start: 2000, total: 400)), true, false); }
        Assert.Equal(2, sent.Count);
    }

    [Fact]
    public void CancelledOrInterruptedCastNeverTurnsWhenBarDisappears()
    {
        var clock = new SimulatedClock(0); var calls = 0;
        var engine = new AutoCastTurnEngine(clock, new(), _ => { calls++; return TurnResult.Ok(); });
        engine.Update(Frame(0, Idle), true, false);
        clock.Advance(100); engine.Update(Frame(100, Active(1000)), true, false);
        clock.Advance(100); engine.Update(Frame(200, Active(900)), true, false);
        clock.Advance(100); engine.Update(Frame(300, Idle), true, false);
        clock.Advance(1000); engine.Update(Frame(1300, Idle), true, false);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("enabled-mid-cast")]
    [InlineData("unknown")]
    [InlineData("stale")]
    [InlineData("gap")]
    [InlineData("revocation")]
    [InlineData("priority")]
    [InlineData("target-switch")]
    [InlineData("map-switch")]
    [InlineData("player-switch")]
    [InlineData("rollback")]
    [InlineData("expired-window")]
    public void UnsafeOrAmbiguousSequenceCannotDispatchLateTurn(string problem)
    {
        var clock = new SimulatedClock(0); var calls = 0;
        var engine = new AutoCastTurnEngine(clock, new(), _ => { calls++; return TurnResult.Ok(); });
        if (problem != "enabled-mid-cast") engine.Update(Frame(0, Idle), true, false);
        clock.Advance(100); engine.Update(Frame(100, Active(250)), true, false);
        clock.Advance(100);
        var sample = Frame(200, Active(150));
        if (problem == "unknown") sample = sample with { Cast = null };
        if (problem == "stale") sample = sample with { CapturedAtMs = -10 };
        if (problem == "map-switch") sample = sample with { Map = 52 };
        if (problem == "player-switch") sample = sample with { Snapshot = sample.Snapshot with { Player = sample.Snapshot.Player with { Id = "other-local" } } };
        if (problem == "target-switch") sample = Frame(200, Active(150), target: "other-target");
        if (problem == "rollback") sample = Frame(200, Active(350));
        if (problem == "expired-window") sample = Frame(200, Active(0));
        if (problem == "gap") { clock.Advance(150); sample = sample with { CapturedAtMs = 350 }; }
        engine.Update(sample, problem != "revocation", problem == "priority");
        clock.Advance(50);
        var final = Frame(clock.NowMs, Active(50));
        if (problem == "map-switch") final = final with { Map = 52 };
        if (problem == "player-switch") final = final with { Snapshot = sample.Snapshot };
        engine.Update(final, true, false);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void EndWindowUsesLatestTargetPositionAndNativeGateCanRecheckContext()
    {
        var clock = new SimulatedClock(0); LiveFrame? current = null; AutoCastTurnEngine? engine = null;
        engine = new AutoCastTurnEngine(clock, new(), direction =>
        {
            Assert.Equal(Direction.Right, direction); Assert.True(engine!.CanDispatch(current, clock.NowMs));
            Assert.False(engine.CanDispatch(current! with { Cast = Idle }, clock.NowMs));
            Assert.False(engine.CanDispatch(current! with { Cast = Active(50, spell: 99) }, clock.NowMs));
            Assert.False(engine.CanDispatch(current! with { Map = 99 }, clock.NowMs));
            Assert.False(engine.CanDispatch(Frame(clock.NowMs, Active(50), target: "other-target"), clock.NowMs));
            Assert.False(engine.CanDispatch(Frame(clock.NowMs, Active(50)), clock.NowMs)); // moved back up
            Assert.False(engine.CanDispatch(current, clock.NowMs + 201));
            return TurnResult.Ok();
        });
        engine.Update(current = Frame(0, Idle), true, false);
        clock.Advance(100); engine.Update(current = Frame(100, Active(250)), true, false);
        clock.Advance(100); engine.Update(current = Frame(200, Active(150)), true, false);
        clock.Advance(100); engine.Update(current = Frame(300, Active(50), targetX: 5, targetY: 0), true, false);
        Assert.Contains("Right", engine.Status); Assert.False(engine.CanDispatch(current, clock.NowMs));
    }

    [Fact]
    public void RefusedInputIsNotRepeatedAndAlreadyAlignedCastDoesNotSendKeys()
    {
        var clock = new SimulatedClock(0); var calls = 0;
        var engine = new AutoCastTurnEngine(clock, new(), _ => { calls++; return TurnResult.Rejected("manual key held"); });
        engine.Update(Frame(0, Idle), true, false);
        clock.Advance(100); engine.Update(Frame(100, Active(250)), true, false);
        clock.Advance(100); engine.Update(Frame(200, Active(150)), true, false);
        clock.Advance(100); engine.Update(Frame(300, Active(50)), true, false);
        engine.Update(Frame(300, Active(50)), true, false);
        Assert.Equal(1, calls);
        clock.Advance(50); engine.Update(Frame(350, Idle), true, false);
        clock.Advance(50); engine.Update(Frame(400, Active(250, start: 2000), Direction.Up), true, false);
        clock.Advance(100); engine.Update(Frame(500, Active(150, start: 2000), Direction.Up), true, false);
        clock.Advance(100); engine.Update(Frame(600, Active(50, start: 2000), Direction.Up), true, false);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void EstimatedTimeBetweenFreshReadsCanReachWindowButCannotReplayConsumedCast()
    {
        var clock = new SimulatedClock(0); var calls = 0;
        var engine = new AutoCastTurnEngine(clock, new(), _ => { calls++; return TurnResult.Ok(); });
        engine.Update(Frame(0, Idle), true, false);
        clock.Advance(100); engine.Update(Frame(100, Active(250)), true, false);
        clock.Advance(100); var sample = Frame(200, Active(150)); engine.Update(sample, true, false);
        clock.Advance(50); engine.Update(sample, true, false);
        Assert.Equal(1, calls);
        clock.Advance(10); engine.Update(Frame(260, Idle), true, false);
        clock.Advance(10); engine.Update(Frame(270, Active(250)), true, false);
        clock.Advance(100); engine.Update(Frame(370, Active(50)), true, false);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ShortAndInvalidCastsAreNotTurnedAtTheirBeginning()
    {
        Assert.False(Active(0, total: 0).Valid);
        Assert.False(Active(2000, total: 1000).Valid);
        Assert.False(Active(-1).Valid);
        Assert.False(Active(500, spell: 0).Valid);
        Assert.False(Active(500, start: 0).Valid);
        var clock = new SimulatedClock(0); var calls = 0;
        var engine = new AutoCastTurnEngine(clock, new() { LeadTimeMs = 250 }, _ => { calls++; return TurnResult.Ok(); });
        engine.Update(Frame(0, Idle), true, false);
        clock.Advance(100); engine.Update(Frame(100, Active(200, total: 200)), true, false);
        clock.Advance(50); engine.Update(Frame(150, Active(150, total: 200)), true, false);
        Assert.Equal(0, calls);
        clock.Advance(100); engine.Update(Frame(250, Active(50, total: 200)), true, false);
        Assert.Equal(1, calls);
        Assert.Throws<ArgumentException>(() => new AutoCastTurnSettings { LeadTimeMs = 0 }.Validate());
        Assert.Throws<ArgumentException>(() => new AutoCastTurnSettings { LeadTimeMs = 251 }.Validate());
    }

    [Theory]
    [InlineData(235, 4717367, 235, 4718357, 4717417, 940)]
    [InlineData(241, 4716045, 241, 4717035, 4716080, 955)]
    [InlineData(241, 4716045, 241, 4717035, 4717036, 0)]
    public void CorrelatedLiveCalibrationFieldsDecodeWithoutSpellWhitelist(int spell, int start, int actor, int end, long now, long remaining)
    {
        var cast = CastObservation.Decode(spell, start, actor, end, now);
        Assert.NotNull(cast); Assert.True(cast.Active); Assert.Equal(990, cast.TotalMs); Assert.Equal(remaining, cast.RemainingMs);
    }

    [Theory]
    [InlineData(235, 1000, 0, 2000, 1100)]
    [InlineData(235, 1000, 241, 2000, 1100)]
    [InlineData(235, 0, 235, 2000, 1100)]
    [InlineData(235, 1000, 235, 999, 1100)]
    [InlineData(235, 1000, 235, 2000, 999)]
    [InlineData(235, 1000, 235, 100000, 1100)]
    [InlineData(0, 0, 0, 2000, 1100)]
    public void PartialMismatchedOrOutOfBoundsCastReadIsUnknownNotIdle(int spell, int start, int actor, int end, long now)
        => Assert.Null(CastObservation.Decode(spell, start, actor, end, now));

    [Theory]
    [InlineData("off")] [InlineData("foreground")] [InlineData("emergency")]
    [InlineData("chat")] [InlineData("stale")] [InlineData("no-target")] [InlineData("unknown")]
    public void DedicatedSafetyGateStillRequiresFreshCastTargetFocusAndNoEmergency(string fault)
    {
        var frame = Frame(0, Idle);
        if (fault == "no-target") frame = frame with { Snapshot = frame.Snapshot with { Target = null } };
        if (fault == "unknown") frame = frame with { Cast = null };
        Assert.False(LiveSafetyGate.CheckCastTurning(frame, fault == "stale" ? 201 : 0,
            fault != "off", fault != "foreground", fault == "emergency", fault == "chat").Allowed);
        Assert.True(LiveSafetyGate.CheckCastTurning(Frame(0, Idle), 0, true, true, false, false).Allowed);
    }

    [Fact]
    public void CastPermissionIsIndependentOfRegularTurnAndMissingFeatureDeniesIt()
    {
        var access = new Core.Security.AccessLease(new SimulatedClock(0));
        access.Apply(new([Core.Security.AppFeature.AutoTurning]), 0, DateTimeOffset.UtcNow);
        Assert.False(access.Allows(Core.Security.AppFeature.AutoCastTurn));
        access.Apply(new([Core.Security.AppFeature.AutoCastTurn]), 0, DateTimeOffset.UtcNow);
        Assert.True(access.Allows(Core.Security.AppFeature.AutoCastTurn)); Assert.False(access.Allows(Core.Security.AppFeature.AutoTurning));
        Assert.Null(GameMemoryProfile.Standalone.CastTiming); Assert.NotNull(GameMemoryProfile.Steam.CastTiming);
    }

    [Fact]
    public void TimingUsesObservationInstantWithoutDoubleSubtractingMemoryReadTime()
    {
        var clock = new SimulatedClock(0); var calls = 0;
        var engine = new AutoCastTurnEngine(clock, new(), _ => { calls++; return TurnResult.Ok(); });
        engine.Update(Frame(0, Idle), true, false);
        clock.Advance(100); engine.Update(Frame(100, Active(250)), true, false);
        clock.Advance(130);
        var frame = Frame(200, Active(120) with { ObservedAtMs = 230 }) with { ReadDurationMs = 30 };
        engine.Update(frame, true, false); Assert.Equal(0, calls);
        clock.Advance(20); engine.Update(frame, true, false); Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(Direction.Up, 0, -5, Direction.Down)]
    [InlineData(Direction.Down, 0, 5, Direction.Up)]
    [InlineData(Direction.Left, -5, 0, Direction.Right)]
    [InlineData(Direction.Right, 5, 0, Direction.Left)]
    public void EndOfCastTurnSupportsAllFourCardinalDirections(Direction expected, double x, double y, Direction initial)
    {
        var clock = new SimulatedClock(0); var sent = new List<Direction>();
        var engine = new AutoCastTurnEngine(clock, new(), d => { sent.Add(d); return TurnResult.Ok(); });
        engine.Update(Frame(0, Idle, initial, targetX: x, targetY: y), true, false);
        clock.Advance(100); engine.Update(Frame(100, Active(250), initial, targetX: x, targetY: y), true, false);
        clock.Advance(100); engine.Update(Frame(200, Active(150), initial, targetX: x, targetY: y), true, false);
        clock.Advance(100); engine.Update(Frame(300, Active(50), initial, targetX: x, targetY: y), true, false);
        Assert.Equal(new[] { expected }, sent);
    }

    [Fact]
    public void WalkingThatOverridesFacingIsCorrectedAgainUntilCastEnds()
    {
        var clock = new SimulatedClock(0); var sent = new List<Direction>();
        var engine = new AutoCastTurnEngine(clock, new() { LeadTimeMs = 200 }, d => { sent.Add(d); return TurnResult.Ok(); });
        engine.Update(Frame(0, Idle), true, false);
        clock.Advance(100); engine.Update(Frame(100, Active(400)), true, false);
        clock.Advance(150); engine.Update(Frame(250, Active(250)), true, false);
        clock.Advance(50); engine.Update(Frame(300, Active(200)), true, false);
        Assert.Single(sent);
        // Observe an aligned frame, then movement overwrites it before the cast finishes.
        clock.Advance(40); engine.Update(Frame(340, Active(160), Direction.Up), true, false);
        Assert.Single(sent);
        clock.Advance(40); engine.Update(Frame(380, Active(120), Direction.Right), true, false);
        Assert.Equal(2, sent.Count);
        engine.Update(Frame(380, Active(120), Direction.Right), true, false);
        Assert.Equal(2, sent.Count); // Never busy-loop on unchanged observations.
        clock.Advance(40); engine.Update(Frame(420, Active(80), Direction.Left), true, false);
        Assert.Equal(3, sent.Count);
        clock.Advance(80); engine.Update(Frame(500, Idle, Direction.Up), true, false);
        clock.Advance(20); engine.Update(Frame(520, Idle, Direction.Right), true, false);
        Assert.Equal(3, sent.Count); // No correction after the cast has completed/cancelled.
        Assert.All(sent, direction => Assert.Equal(Direction.Up, direction));
    }

    [Fact]
    public void InitiallyAlignedCastRemainsWatchedAndMovementCanTriggerFirstCorrectionLate()
    {
        var clock = new SimulatedClock(0); var calls = 0;
        var engine = new AutoCastTurnEngine(clock, new(), _ => { calls++; return TurnResult.Ok(); });
        engine.Update(Frame(0, Idle, Direction.Up), true, false);
        clock.Advance(100); engine.Update(Frame(100, Active(250), Direction.Up), true, false);
        clock.Advance(100); engine.Update(Frame(200, Active(150), Direction.Up), true, false);
        clock.Advance(50); engine.Update(Frame(250, Active(100), Direction.Up), true, false);
        Assert.Equal(0, calls);
        clock.Advance(50); engine.Update(Frame(300, Active(50), Direction.Down), true, false);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void AlreadyOwnedTurnMayCoverDeadlineButCannotStartLateOrSurviveCancel()
    {
        var clock = new SimulatedClock(0); AutoCastTurnEngine? engine = null;
        engine = new AutoCastTurnEngine(clock, new(), _ =>
        {
            var aligned = Frame(300, Active(50), Direction.Up);
            Assert.True(engine!.CanMaintain(aligned, 300));
            Assert.False(engine.CanDispatch(aligned, 350));
            Assert.True(engine.CanMaintain(aligned, 350)); // deadline covered by an existing owned key
            Assert.True(engine.CanMaintain(aligned, 399));
            Assert.False(engine.CanMaintain(aligned, 401)); // <=50ms hard limit, even if actor flag is stuck
            Assert.False(engine.CanMaintain(Frame(320, Idle, Direction.Up), 320));
            Assert.False(engine.CanMaintain(Frame(320, null, Direction.Up), 320));
            Assert.False(engine.CanMaintain(Frame(320, Active(30, spell: 99), Direction.Up), 320));
            Assert.False(engine.CanMaintain(Frame(320, Active(30), Direction.Up, target: "other-target"), 320));
            return TurnResult.Ok();
        });
        engine.Update(Frame(0, Idle), true, false);
        clock.Advance(100); engine.Update(Frame(100, Active(250)), true, false);
        clock.Advance(100); engine.Update(Frame(200, Active(150)), true, false);
        clock.Advance(100); engine.Update(Frame(300, Active(50)), true, false);
        Assert.False(engine.CanMaintain(Frame(300, Active(50), Direction.Up), 300)); // no key owned outside callback
    }

    [Fact]
    public void FreshObservedDriftOrChangedAimEndsHoldToPermitAnotherCorrection()
    {
        var clock = new SimulatedClock(0); AutoCastTurnEngine? engine = null;
        engine = new AutoCastTurnEngine(clock, new(), _ =>
        {
            Assert.True(engine!.CanMaintain(Frame(300, Active(100)), 300)); // Give initial input time to be polled.
            Assert.True(engine.CanMaintain(Frame(340, Active(60)), 340));
            Assert.False(engine.CanMaintain(Frame(341, Active(59), Direction.Down), 341));
            Assert.True(engine.CanMaintain(Frame(341, Active(59), Direction.Up), 341));
            Assert.False(engine.CanMaintain(Frame(320, Active(80), targetX: 5, targetY: 0), 320));
            return TurnResult.Ok();
        });
        engine.Update(Frame(0, Idle), true, false);
        clock.Advance(100); engine.Update(Frame(100, Active(300)), true, false);
        clock.Advance(100); engine.Update(Frame(200, Active(200)), true, false);
        clock.Advance(100); engine.Update(Frame(300, Active(100)), true, false);
    }

    [Fact]
    public void ReapplyingAfterRealHoldLeavesTimeForGameToObserveKeyUp()
    {
        var clock = new SimulatedClock(0); var calls = 0;
        var engine = new AutoCastTurnEngine(clock, new() { LeadTimeMs = 200 }, _ =>
        {
            if (++calls == 1) clock.Advance(60); // Model the adapter owning the key until a fresh drift sample.
            return TurnResult.Ok();
        });
        engine.Update(Frame(0, Idle), true, false);
        clock.Advance(100); engine.Update(Frame(100, Active(500)), true, false);
        clock.Advance(150); engine.Update(Frame(250, Active(350)), true, false);
        clock.Advance(150); engine.Update(Frame(400, Active(200)), true, false);
        Assert.Equal(460, clock.NowMs); Assert.Equal(1, calls);
        engine.Update(Frame(460, Active(140)), true, false); Assert.Equal(1, calls);
        clock.Advance(10); engine.Update(Frame(470, Active(130)), true, false); Assert.Equal(1, calls);
        clock.Advance(10); engine.Update(Frame(480, Active(120)), true, false); Assert.Equal(2, calls);
    }
}
