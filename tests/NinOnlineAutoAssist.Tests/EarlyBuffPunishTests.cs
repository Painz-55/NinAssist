using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Logging;
using NinOnlineAutoAssist.Core.Events;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Simulation;

namespace NinOnlineAutoAssist.Tests;

public sealed class EarlyBuffPunishTests
{
    private sealed class Rig
    {
        public SimulatedClock Clock { get; } = new(0);
        public SimulatedActionExecutor Executor { get; }
        public MemoryLogger Log { get; } = new();
        public EarlyBuffPunishEngine Engine { get; }
        public Rig(EarlyPunishOptions? options = null)
        {
            Executor = new SimulatedActionExecutor(Clock);
            Engine = new EarlyBuffPunishEngine(Executor, Clock, options, Log);
        }
        public void At(long time, bool active = false, int? duration = null, bool enabled = true,
            bool authorized = true, bool safe = true, Func<LiveFrame, LiveFrame>? mutate = null)
        {
            Clock.Advance(time - Clock.NowMs);
            var frame = MakeFrame(time, active, duration);
            Engine.Update(mutate is null ? frame : mutate(frame), enabled, authorized, safe, "test safety block");
        }
        public void ActiveUntil(long time)
        {
            for (var t = 0L; t <= time; t += 200) At(t, true, Math.Max(0, 7 - (int)(t / 1000)));
        }
        public void Trigger()
        {
            ActiveUntil(2000);
            At(2200);
            Assert.Equal(EarlyPunishState.RemovalPending, Engine.State);
            Assert.Empty(Executor.History);
            At(2400);
        }
    }

    private static LiveFrame MakeFrame(long time, bool active, int? duration) => new(
        new GameSnapshot { Player = new PlayerState { Id = "local", Facing = Direction.None, Resources = 100 },
            Target = new TargetInfo { Id = "target", IsTargeted = true, PositionX = 100 },
            ActiveBuffs = active ? new[] { "Substitution Technique" } : Array.Empty<string>() },
        time, 51, "Player", "Target", active, duration, 0);

    [Fact]
    public void EarlyDisappearanceSendsAQSWithoutFacingOrPositionChange()
    {
        var rig = new Rig(); rig.Trigger();
        Assert.Equal("A", Assert.Single(rig.Executor.History).Key);
        Assert.True(rig.Engine.BlocksAutoTurning);
        rig.At(2520); rig.At(2640);
        Assert.Equal(new[] { "A", "Q", "S" }, rig.Executor.History.Select(a => a.Key));
        Assert.Equal(EarlyPunishState.Completed, rig.Engine.State);
        Assert.True(rig.Engine.EstimatedRemainingAtRemovalMs > 1000);
        Assert.Contains(rig.Log.Snapshot(), e => e.Message.Contains("HEURÍSTICA de remoção antecipada"));
        Assert.DoesNotContain(rig.Log.Snapshot(), e => e.Origin == EventOrigin.Confirmed);
    }

    [Fact]
    public void CompletedCycleDoesNotRepeatWhileBuffRemainsAbsent()
    {
        var rig = new Rig(); rig.Trigger(); rig.At(2520); rig.At(2640);
        for (var t = 2800; t <= 4000; t += 200) rig.At(t);
        Assert.Equal(3, rig.Executor.History.Count);
        Assert.False(rig.Engine.BlocksAutoTurning);
    }

    [Fact]
    public void NaturalExpiryNeverExecutes()
    {
        var rig = new Rig(); rig.ActiveUntil(6800); rig.At(7000); rig.At(7200);
        Assert.Empty(rig.Executor.History);
        Assert.Equal(EarlyPunishState.Expired, rig.Engine.State);
    }

    [Fact]
    public void LastSecondRemovalIsRejectedByRoundingMargin()
    {
        var rig = new Rig(); rig.ActiveUntil(5800); rig.At(6000); rig.At(6200);
        Assert.Empty(rig.Executor.History);
        Assert.Equal(EarlyPunishState.Expired, rig.Engine.State);
    }

    [Fact]
    public void SingleTransientAbsenceDoesNotDispatch()
    {
        var rig = new Rig(); rig.ActiveUntil(2000); rig.At(2200); rig.At(2400, true, 5);
        Assert.Empty(rig.Executor.History);
        Assert.Equal(EarlyPunishState.WatchingBuff, rig.Engine.State);
    }

    [Fact]
    public void DuplicateFrameCannotConfirmAbsenceButCanSchedulePendingKeys()
    {
        var rig = new Rig(); rig.ActiveUntil(2000); rig.At(2200);
        rig.Engine.Update(MakeFrame(2200, false, null), true, true, true);
        Assert.Empty(rig.Executor.History);
        rig.At(2400);
        rig.Clock.Advance(120);
        rig.Engine.Update(MakeFrame(2400, false, null), true, true, true);
        Assert.Equal(2, rig.Executor.History.Count);
        rig.At(2520);
        Assert.Equal(new[] { "A", "Q" }, rig.Executor.History.Select(a => a.Key));
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void DisabledPermissionOrGateCancelsAndDoesNotReplay(bool enabled, bool authorized, bool safe)
    {
        var rig = new Rig(); rig.Trigger();
        rig.At(2500, enabled: enabled, authorized: authorized, safe: safe);
        rig.At(2700); rig.At(2900);
        Assert.Single(rig.Executor.History);
        Assert.Equal(EarlyPunishState.Cancelled, rig.Engine.State);
    }

    [Fact]
    public void NoObservedBuffMeansNoTrigger()
    {
        var rig = new Rig(); rig.At(0); rig.At(200); rig.At(400);
        Assert.Empty(rig.Executor.History);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void NoInitialAuthorizationCannotSeedALaterTrigger(bool enabled, bool authorized, bool safe)
    {
        var rig = new Rig();
        rig.At(0, true, 7, enabled, authorized, safe);
        rig.At(200); rig.At(400);
        Assert.Empty(rig.Executor.History);
    }

    [Fact]
    public void NewObservedBuffCanStartAnotherIndependentCycle()
    {
        var rig = new Rig(); rig.Trigger(); rig.At(2520); rig.At(2640); rig.At(2800);
        rig.At(3000, true, 7); rig.At(3200); rig.At(3400); rig.At(3520); rig.At(3640);
        Assert.Equal(new[] { "A", "Q", "S", "A", "Q", "S" }, rig.Executor.History.Select(a => a.Key));
    }

    [Fact]
    public void MissingFrameCannotBecomeBuffRemoval()
    {
        var rig = new Rig(); rig.ActiveUntil(2000);
        rig.Clock.Advance(200); rig.Engine.Update(null, true, true, true);
        rig.At(2400); rig.At(2600);
        Assert.Empty(rig.Executor.History);
    }

    [Fact]
    public void ReadGapCannotBecomeEarlyRemoval()
    {
        var rig = new Rig(); rig.ActiveUntil(2000); rig.At(2600); rig.At(2800);
        Assert.Empty(rig.Executor.History);
        Assert.Equal(EarlyPunishState.Cancelled, rig.Engine.State);
    }

    [Theory]
    [InlineData("map")]
    [InlineData("player")]
    [InlineData("dead")]
    public void InvalidIdentityStopsRemainingKeys(string change)
    {
        var rig = new Rig(); rig.Trigger();
        rig.At(2520, mutate: f => change switch
        {
            "map" => f with { Map = 52 },
            "player" => f with { Snapshot = f.Snapshot with { Player = f.Snapshot.Player with { Id = "other" } } },
            "dead" => f with { Snapshot = f.Snapshot with { Player = f.Snapshot.Player with { IsAlive = false } } },
            "no-target" => f with { Snapshot = f.Snapshot with { Target = null } },
            _ => f with { Snapshot = f.Snapshot with { Target = f.Snapshot.Target! with { Id = "replacement" } } }
        });
        Assert.Single(rig.Executor.History);
        Assert.Equal(EarlyPunishState.Cancelled, rig.Engine.State);
    }

    [Fact]
    public void UnknownOrZeroDurationIsNotAnEarlyExpiryEstimate()
    {
        foreach (var duration in new int?[] { null, 0, -1, 1000 })
        {
            var rig = new Rig(); rig.At(0, true, duration); rig.At(200); rig.At(400);
            Assert.Empty(rig.Executor.History);
        }
    }

    [Fact]
    public void UnknownDurationInvalidatesAnOlderEstimate()
    {
        var rig = new Rig(); rig.At(0, true, 7); rig.At(200, true, null); rig.At(400); rig.At(600);
        Assert.Empty(rig.Executor.History);
    }

    [Fact]
    public void KeyPacingIsRespected()
    {
        var rig = new Rig(); rig.Trigger(); rig.At(2500);
        Assert.Single(rig.Executor.History);
        rig.At(2520); rig.At(2600);
        Assert.Equal(2, rig.Executor.History.Count);
        rig.At(2640);
        Assert.Equal(3, rig.Executor.History.Count);
    }

    [Fact]
    public void RejectedQStopsS()
    {
        var rig = new Rig(); rig.Trigger(); rig.Executor.RejectNextRequest = true; rig.At(2520); rig.At(2640);
        Assert.Single(rig.Executor.History);
        Assert.Equal(EarlyPunishState.Cancelled, rig.Engine.State);
    }

    [Fact]
    public void BuffReturnDuringSequenceStopsRemainingKeys()
    {
        var rig = new Rig(); rig.Trigger(); rig.At(2520, true, 4);
        Assert.Single(rig.Executor.History);
        Assert.Equal(EarlyPunishState.Cancelled, rig.Engine.State);
    }

    [Fact]
    public void SequenceTimeoutStopsS()
    {
        var rig = new Rig(new EarlyPunishOptions { SequenceTimeoutMs = 360 }); rig.Trigger();
        rig.At(2700); rig.At(2800);
        Assert.Equal(new[] { "A", "Q" }, rig.Executor.History.Select(a => a.Key));
        Assert.Equal(EarlyPunishState.Cancelled, rig.Engine.State);
    }

    [Fact]
    public void ResetAfterCandidateDoesNotReplay()
    {
        var rig = new Rig(); rig.ActiveUntil(2000); rig.At(2200); rig.Engine.Reset(); rig.At(2400); rig.At(2600);
        Assert.Empty(rig.Executor.History);
    }

    [Fact]
    public void ExecutorExceptionCancelsInsteadOfEscaping()
    {
        var clock = new SimulatedClock(0);
        var engine = new EarlyBuffPunishEngine(new ThrowingExecutor(), clock);
        engine.Update(MakeFrame(0, true, 7), true, true, true);
        clock.Advance(200); engine.Update(MakeFrame(200, false, null), true, true, true);
        clock.Advance(200); engine.Update(MakeFrame(400, false, null), true, true, true);
        Assert.Equal(EarlyPunishState.Cancelled, engine.State);
    }

    private sealed class ThrowingExecutor : IActionExecutor
    {
        public ActionResult Execute(string actionId, string boundKey) => throw new IOException("test failure");
    }
}
