using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Events;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Core.SubPunish;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Tests;

public sealed class LiveIntegrationTests
{
    private static LiveFrame Frame(long time = 1000, bool buff = false, double y = 0, int map = 51) => new(
        new GameSnapshot { Player = new PlayerState { Id = "local", PositionY = y, Facing = Direction.Down, Resources = 100 },
            Target = new TargetInfo { Id = "target", PositionY = 1, IsTargeted = true },
            ActiveBuffs = buff ? new[] { "Substitution Technique" } : Array.Empty<string>() },
        time, map, "Player", "Target", buff, buff ? 5 : null, 100);

    [Fact]
    public void GateAllowsOnlyFreshArmedForegroundData() => Assert.True(
        LiveSafetyGate.Check(Frame(), 1100, true, true, false, false, false).Allowed);

    [Theory]
    [InlineData(false, true, false, false, false)]
    [InlineData(true, false, false, false, false)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, true, false, false, true)]
    public void GateGuardsIgnoreManualMovement(bool foreground, bool hold, bool stop, bool movement, bool text) =>
        Assert.Equal(foreground && hold && !stop && !text,
            LiveSafetyGate.Check(Frame(), 1100, foreground, hold, stop, movement, text).Allowed);

    [Theory]
    [InlineData(1501)]
    [InlineData(999)]
    public void GateRejectsStaleOrFutureData(long now) => Assert.False(
        LiveSafetyGate.Check(Frame(), now, true, true, false, false, false).Allowed);

    [Fact]
    public void SlowReadCannotBeMadeFreshByPublicationTime() => Assert.False(
        LiveSafetyGate.Check(Frame() with { ReadDurationMs = 700 }, 1100, true, true, false, false, false).Allowed);

    [Fact]
    public void GateRejectsMissingDeadOrUntargetedState()
    {
        Assert.False(LiveSafetyGate.Check(null, 1000, true, true, false, false, false).Allowed);
        var frame = Frame();
        Assert.False(LiveSafetyGate.Check(frame with { Snapshot = frame.Snapshot with { Target = null } },
            1000, true, true, false, false, false).Allowed);
        Assert.False(LiveSafetyGate.Check(frame with { Snapshot = frame.Snapshot with
            { Player = frame.Snapshot.Player with { IsAlive = false } } }, 1000, true, true, false, false, false).Allowed);
    }

    [Fact]
    public void BuffObservationNeverManufacturesThreatHitOrAttacker()
    {
        var detector = new LiveEventDetector();
        var events = detector.Observe(Frame(buff: true), false);
        Assert.Single(events);
        Assert.Equal(CombatEventKind.BuffConfirmed, events[0].Kind);
        Assert.Null(events[0].AttackerId);
        Assert.Equal(EventOrigin.Observed, events[0].Origin);
        Assert.Empty(detector.Observe(Frame(1100, true), false));
    }

    [Fact]
    public void NaturalExpiryIsNotTeleportOrExplicitTrigger()
    {
        var detector = new LiveEventDetector();
        detector.Observe(Frame(buff: true), false);
        Assert.Empty(detector.Observe(Frame(1100), false)); // bounded grace period for delayed position data
        var events = detector.Observe(Frame(1500), false);
        Assert.Equal(CombatEventKind.BuffExpired, Assert.Single(events).Kind);
    }

    [Fact]
    public void ExpiringZeroDurationBuffDoesNotTurnUnrelatedJumpIntoTrigger()
    {
        var detector = new LiveEventDetector();
        detector.Observe(Frame(buff: true) with { SubstitutionDuration = 0 }, false);
        Assert.Equal(CombatEventKind.BuffExpired, Assert.Single(detector.Observe(Frame(1100, y: 2), false)).Kind);
    }

    [Fact]
    public void TeleportPrecedesBuffDisappearanceAndDoesNotIdentifyAttacker()
    {
        var detector = new LiveEventDetector();
        detector.Observe(Frame(buff: true), false);
        var events = detector.Observe(Frame(1100, y: 2), false);
        Assert.Equal(new[] { CombatEventKind.TeleportObserved, CombatEventKind.BuffExpired }, events.Select(e => e.Kind));
        Assert.All(events, e => Assert.Null(e.AttackerId));
    }

    [Fact]
    public void BuffObservationDoesNotRequireTargetOrInputAuthorization()
    {
        var frame = Frame(buff: true);
        frame = frame with { Snapshot = frame.Snapshot with { Target = null } };
        Assert.False(LiveSafetyGate.Check(frame, 1100, false, false, false, false, false).Allowed);
        var observed = LiveBuffObservation.FromFrame(frame, 1100);
        Assert.Equal(BuffPresence.Active, observed.Presence);
        Assert.Equal(5, observed.DurationSeconds);
    }

    [Fact]
    public void MissingOrStaleBuffDataIsUnknownNotInactive()
    {
        Assert.Equal(BuffPresence.Unknown, LiveBuffObservation.FromFrame(null, 1100).Presence);
        Assert.Equal(BuffPresence.Unknown, LiveBuffObservation.FromFrame(Frame(buff: true), 1501).Presence);
        Assert.Equal(BuffPresence.Unknown, LiveBuffObservation.FromFrame(Frame(buff: true) with { ReadDurationMs = 600 }, 1100).Presence);
        Assert.Equal(BuffPresence.Inactive, LiveBuffObservation.FromFrame(Frame(), 1100).Presence);
    }

    [Fact]
    public void BuffRemovalBeforePositionUpdateStillProducesOrderedEvidence()
    {
        var detector = new LiveEventDetector();
        detector.Observe(Frame(buff: true), false);
        Assert.Empty(detector.Observe(Frame(1100), false));
        var events = detector.Observe(Frame(1200, y: 2), false);
        Assert.Equal(new[] { CombatEventKind.TeleportObserved, CombatEventKind.BuffExpired }, events.Select(e => e.Kind));
        Assert.Empty(detector.Observe(Frame(1300, y: 2), false)); // no duplicate trigger
    }

    [Fact]
    public void PositionUpdateBeforeBuffRemovalIsAlsoCorrelated()
    {
        var detector = new LiveEventDetector();
        detector.Observe(Frame(buff: true), false);
        Assert.Equal(CombatEventKind.TeleportObserved, Assert.Single(detector.Observe(Frame(1100, true, y: 2), false)).Kind);
        Assert.Equal(CombatEventKind.BuffExpired, Assert.Single(detector.Observe(Frame(1200, y: 2), false)).Kind);
    }

    [Fact]
    public void UncorroboratedJumpIsCancelledInsteadOfCreditedAtNaturalExpiry()
    {
        var detector = new LiveEventDetector();
        detector.Observe(Frame(buff: true), false);
        detector.Observe(Frame(1100, true, y: 2), false);
        Assert.Equal(CombatEventKind.ManualInterrupt, Assert.Single(detector.Observe(Frame(1501, true, y: 2), false)).Kind);
    }

    [Fact]
    public void LateJumpAndManualMovementCannotCompleteDeferredRemoval()
    {
        var detector = new LiveEventDetector();
        detector.Observe(Frame(buff: true), false);
        detector.Observe(Frame(1100), false);
        Assert.Equal(CombatEventKind.BuffExpired, Assert.Single(detector.Observe(Frame(1501, y: 2), false)).Kind);
        detector.Reset();
        detector.Observe(Frame(buff: true), false);
        detector.Observe(Frame(1100), false);
        Assert.Equal(CombatEventKind.BuffExpired, Assert.Single(detector.Observe(Frame(1200, y: 2), true)).Kind);
    }

    [Fact]
    public void DeferredRemovalCannotSurviveMapChange()
    {
        var detector = new LiveEventDetector();
        detector.Observe(Frame(buff: true), false);
        detector.Observe(Frame(1100), false);
        Assert.Equal(CombatEventKind.ManualInterrupt, Assert.Single(detector.Observe(Frame(1200, y: 2, map: 52), false)).Kind);
        Assert.Empty(detector.Observe(Frame(1300, y: 2, map: 52), false));
    }

    [Fact]
    public void TransientAbsentBuffThenReturnDoesNotManufactureNewActivation()
    {
        var detector = new LiveEventDetector();
        detector.Observe(Frame(buff: true), false);
        detector.Observe(Frame(1100), false);
        Assert.Empty(detector.Observe(Frame(1200, true), false));
    }

    [Fact]
    public void MovementMapChangeAndReadGapCannotBecomeTeleportEvidence()
    {
        var detector = new LiveEventDetector();
        detector.Observe(Frame(buff: true), false);
        Assert.DoesNotContain(detector.Observe(Frame(1100, y: 2), true), e => e.Kind == CombatEventKind.TeleportObserved);
        detector.Reset(); detector.Observe(Frame(buff: true), false);
        Assert.Equal(CombatEventKind.ManualInterrupt, Assert.Single(detector.Observe(Frame(1100, y: 2, map: 52), false)).Kind);
        detector.Reset(); detector.Observe(Frame(buff: true), false);
        Assert.Equal(CombatEventKind.ManualInterrupt, Assert.Single(detector.Observe(Frame(2000, y: 2), false)).Kind);
    }

    private static TestRig AssistedRig(bool orientationOnly = false, bool followup = false) => new(
        sp: new SubPunishOptions { Enabled = true, AutoPrepareSub = false,
            AdoptManualSubstitutionForSelectedTarget = true, OrientationOnly = orientationOnly },
        ca: new CounterAttackOptions { GlobalCooldownMs = 0,
            AttackSequence = followup
                ? new[] { new CounterAttackAction { Id = "basic_attack", Key = "A", MaxRange = 1.5 },
                    new CounterAttackAction { Id = "followup_hotbar_Q", Key = "Q", MaxRange = 1.5 } }
                : new[] { new CounterAttackAction { Id = "basic_attack", Key = "A", MaxRange = 1.5 } } });

    [Fact]
    public void DeferredPositionEvidenceReachesCoreWithoutLosingManualBuffState()
    {
        var rig = AssistedRig();
        var detector = new LiveEventDetector();
        foreach (var frame in new[] { Frame(buff: true), Frame(1100), Frame(1200, y: 2) })
        {
            rig.Clock.Advance(100);
            foreach (var evt in detector.Observe(frame, false)) rig.SubPunish.HandleEvent(evt, frame.Snapshot);
            rig.SubPunish.Tick(frame.Snapshot);
        }
        Assert.Equal(SubPunishState.TurningToAttacker, rig.SubPunish.State);
        Assert.Empty(rig.Executor.History);
        var aligned = Frame(1300, y: 2).Snapshot;
        aligned = aligned with { Player = aligned.Player with { Facing = Direction.Up } };
        rig.SubPunish.Tick(aligned);
        rig.SubPunish.Tick(aligned);
        Assert.Equal(SubPunishState.Completed, rig.SubPunish.State);
        Assert.Equal("A", Assert.Single(rig.Executor.History).Key);
    }

    [Fact]
    public void AssistedManualSubCanReachOneCounterWithoutFakeThreatOrAttacker()
    {
        var rig = AssistedRig();
        var before = Frame(buff: true).Snapshot;
        rig.SubPunish.HandleEvent(CombatEvent.Create(CombatEventKind.BuffConfirmed, 0, EventOrigin.Observed), before);
        rig.SubPunish.Tick(before);
        Assert.Equal(SubPunishState.SubActive, rig.SubPunish.State);
        Assert.Null(rig.SubPunish.LastAttackerId);
        Assert.Empty(rig.Executor.History);
        var after = Frame(1100, y: 2).Snapshot;
        rig.Clock.Advance(100);
        rig.SubPunish.HandleEvent(new CombatEvent { Kind = CombatEventKind.TeleportObserved, TimestampMs = 100,
            PositionX = 0, PositionY = 2 }, after);
        rig.SubPunish.HandleEvent(CombatEvent.Create(CombatEventKind.BuffExpired, 100, EventOrigin.Observed), after);
        Assert.Equal(SubPunishState.TurningToAttacker, rig.SubPunish.State);
        Assert.Empty(rig.Executor.History); // No offensive input before observed facing confirmation.
        after = after with { Player = after.Player with { Facing = Direction.Up } };
        rig.SubPunish.Tick(after);
        rig.SubPunish.Tick(after);
        Assert.Equal(SubPunishState.Completed, rig.SubPunish.State);
        Assert.Equal("basic_attack", Assert.Single(rig.Executor.History).ActionId);
        Assert.Equal(0, rig.Metrics.ActivationsRequested);
    }

    [Fact]
    public void AssistedTargetChangeCancelsBeforeAnyAttack()
    {
        var rig = AssistedRig();
        var snapshot = Frame(buff: true).Snapshot;
        rig.SubPunish.HandleEvent(CombatEvent.Create(CombatEventKind.BuffConfirmed, 0, EventOrigin.Observed), snapshot);
        rig.SubPunish.Tick(snapshot with { Target = snapshot.Target! with { Id = "replacement" } });
        Assert.Equal(SubPunishState.Cancelled, rig.SubPunish.State);
        Assert.Empty(rig.Executor.History);
        Assert.Null(rig.SubPunish.LastAttackerId);
    }

    [Fact]
    public void ManualSubNaturalExpiryNeverSendsAOrQ()
    {
        var rig = AssistedRig(followup: true);
        var snapshot = Frame(buff: true).Snapshot;
        rig.SubPunish.HandleEvent(CombatEvent.Create(CombatEventKind.BuffConfirmed, 0, EventOrigin.Observed), snapshot);
        rig.SubPunish.Tick(snapshot);
        rig.SubPunish.HandleEvent(CombatEvent.Create(CombatEventKind.BuffExpired, 100, EventOrigin.Observed), Frame().Snapshot);
        Assert.Equal(SubPunishState.Idle, rig.SubPunish.State);
        Assert.Empty(rig.Executor.History);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfirmedManualFlowHonorsOrientationOnlyAndPacesAThenQ(bool orientationOnly)
    {
        var rig = AssistedRig(orientationOnly, followup: true);
        var before = Frame(buff: true).Snapshot;
        rig.SubPunish.HandleEvent(CombatEvent.Create(CombatEventKind.BuffConfirmed, 0, EventOrigin.Observed), before);
        rig.SubPunish.Tick(before);
        var after = Frame(1100, y: 2).Snapshot;
        rig.Clock.Advance(100);
        rig.SubPunish.HandleEvent(new CombatEvent { Kind = CombatEventKind.TeleportObserved, TimestampMs = 100,
            PositionX = 0, PositionY = 2 }, after);
        rig.SubPunish.HandleEvent(CombatEvent.Create(CombatEventKind.BuffExpired, 100, EventOrigin.Observed), after);
        after = after with { Player = after.Player with { Facing = Direction.Up } };
        rig.SubPunish.Tick(after);
        rig.SubPunish.Tick(after);
        if (orientationOnly)
        {
            Assert.Equal(SubPunishState.Completed, rig.SubPunish.State);
            Assert.Empty(rig.Executor.History);
            return;
        }
        Assert.Equal("A", Assert.Single(rig.Executor.History).Key);
        rig.Clock.Advance(50);
        rig.SubPunish.Tick(after);
        Assert.Single(rig.Executor.History);
        rig.Clock.Advance(100);
        rig.SubPunish.Tick(after);
        Assert.Equal(new[] { "A", "Q" }, rig.Executor.History.Select(h => h.Key));
        Assert.Equal(SubPunishState.Completed, rig.SubPunish.State);
    }

    [Fact]
    public void ManualBuffRequiresExplicitModeAndActualBuffInSnapshot()
    {
        var rig = new TestRig();
        rig.SubPunish.HandleEvent(CombatEvent.Create(CombatEventKind.BuffConfirmed, 0, EventOrigin.Observed), Frame(buff: true).Snapshot);
        Assert.Equal(SubPunishState.Idle, rig.SubPunish.State);
        rig = AssistedRig();
        rig.SubPunish.HandleEvent(CombatEvent.Create(CombatEventKind.BuffConfirmed, 0, EventOrigin.Observed), Frame().Snapshot);
        Assert.Equal(SubPunishState.Idle, rig.SubPunish.State);
    }

    [Theory]
    [InlineData(0, Direction.Up)]
    [InlineData(1, Direction.Down)]
    [InlineData(2, Direction.Left)]
    [InlineData(3, Direction.Right)]
    [InlineData(4, Direction.None)]
    [InlineData(255, Direction.None)]
    public void DirectionMappingDoesNotClaimCardinalAlignmentForDiagonals(byte raw, Direction expected) =>
        Assert.Equal(expected, ReadOnlyGameStateProvider.DecodeFacing(raw));
}
