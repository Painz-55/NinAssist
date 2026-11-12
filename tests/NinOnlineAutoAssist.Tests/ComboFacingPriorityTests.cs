using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Simulation;

namespace NinOnlineAutoAssist.Tests;

public sealed class ComboFacingPriorityTests
{
    private static LiveFrame Frame(long at, Direction facing = Direction.Down, bool active = false) => new(
        new GameSnapshot
        {
            Player = new PlayerState { Id = "local", Facing = facing },
            Target = new TargetInfo { Id = "target", IsTargeted = true, PositionY = -5 }
        }, at, 51, "Player", "Target", active, active ? 5 : null, 0);

    private sealed class RecordingInputs(List<string> history) : IOrientationController, IActionExecutor
    {
        public bool RejectTurn { get; set; }
        public TurnResult RequestTurn(Direction direction)
        {
            history.Add($"Turn {direction}");
            return RejectTurn ? TurnResult.Rejected("test rejection") : TurnResult.Ok();
        }
        public ActionResult Execute(string actionId, string boundKey)
        { history.Add(boundKey); return ActionResult.Ok(); }
    }

    private sealed class PreparedExecutor(ComboFacingCoordinator facing, IActionExecutor inner) : IActionExecutor
    {
        public ActionResult Execute(string actionId, string boundKey)
        {
            var prepared = facing.BeforeAction();
            return prepared.Accepted ? inner.Execute(actionId, boundKey) : prepared;
        }
    }

    [Fact]
    public void TurnIsSentBeforeFirstKeyAndBeforeLaterSpellWhenTargetMoves()
    {
        var clock = new SimulatedClock(0);
        var history = new List<string>();
        var inputs = new RecordingInputs(history);
        LiveFrame? latest = null;
        var preparation = new ComboFacingCoordinator(inputs, clock, () => latest, () => true);
        var engine = new EarlyBuffPunishEngine(new PreparedExecutor(preparation, inputs), clock,
            new EarlyPunishOptions { FirstToSecondMs = 0, SecondToThirdMs = 200 });
        void Tick(long time, Direction facing = Direction.Down, bool active = false, bool rightTarget = false)
        {
            clock.Advance(time - clock.NowMs);
            latest = Frame(time, facing, active);
            if (rightTarget) latest = latest with { Snapshot = latest.Snapshot with
            { Target = latest.Snapshot.Target! with { PositionX = 5, PositionY = 0 } } };
            engine.Update(latest, true, true, true);
        }
        Tick(0, active: true); Tick(50); Tick(100);
        Assert.Equal(new[] { "Turn Up" }, history);
        engine.Update(latest, true, true, true); // duplicate/unrefreshed facing cannot release the spell
        Assert.Single(history);
        Tick(101, Direction.Up);
        Assert.Equal(new[] { "Turn Up", "A", "Q" }, history);
        Tick(300, Direction.Up);
        Assert.Equal(3, history.Count); // Q→S timer still applies
        Tick(301, Direction.Up, rightTarget: true);
        Assert.Equal(new[] { "Turn Up", "A", "Q", "Turn Right" }, history);
        Tick(302, Direction.Right, rightTarget: true);
        Assert.Equal(new[] { "Turn Up", "A", "Q", "Turn Right", "S" }, history);
        Assert.Equal(EarlyPunishState.Completed, engine.State);
    }

    [Theory]
    [InlineData("no-target")]
    [InlineData("already-facing")]
    public void NoUnnecessaryTurnOrDelayIsAdded(string condition)
    {
        var history = new List<string>();
        var frame = Frame(0, condition == "already-facing" ? Direction.Up : Direction.Down);
        if (condition == "no-target") frame = frame with { Snapshot = frame.Snapshot with { Target = null } };
        var preparation = new ComboFacingCoordinator(new RecordingInputs(history), new SimulatedClock(0),
            () => frame, () => true);
        Assert.True(preparation.BeforeAction().Accepted);
        Assert.Empty(history);
    }

    [Fact]
    public void DeniedPreparatoryTurnCannotReleaseOffensiveKeyToSelectedTarget()
    {
        var history = new List<string>();
        var frame = Frame(0);
        var input = new RecordingInputs(history);
        var executor = new PreparedExecutor(new ComboFacingCoordinator(input, new SimulatedClock(0),
            () => frame, () => false), input);
        Assert.False(executor.Execute("basic_attack", "A").Accepted);
        Assert.Empty(history);
    }

    [Fact]
    public void AnAlignedButPreTurnSnapshotCannotBeUsedAsPostTurnConfirmation()
    {
        var clock = new SimulatedClock(100);
        var history = new List<string>();
        var frame = Frame(100);
        var preparation = new ComboFacingCoordinator(new RecordingInputs(history), clock, () => frame, () => true);
        Assert.True(preparation.BeforeAction().Retryable);
        frame = Frame(100, Direction.Up); // timestamp is still the pre-dispatch sample
        clock.Advance(10);
        Assert.True(preparation.BeforeAction().Retryable);
        frame = Frame(110, Direction.Up);
        Assert.True(preparation.BeforeAction().Accepted);
        Assert.Single(history);
    }

    [Fact]
    public void UnconfirmedTurnIsNotRepeatedAndNeverReleasesAnOffensiveKey()
    {
        var clock = new SimulatedClock(0);
        var history = new List<string>();
        var inputs = new RecordingInputs(history);
        var frame = Frame(0);
        var preparation = new ComboFacingCoordinator(inputs, clock, () => frame, () => true);
        var executor = new PreparedExecutor(preparation, inputs);
        Assert.True(executor.Execute("basic_attack", "A").Retryable);
        clock.Advance(20); frame = Frame(20);
        Assert.True(executor.Execute("basic_attack", "A").Retryable);
        Assert.Single(history);
        clock.Advance(20); frame = Frame(40); inputs.RejectTurn = true;
        Assert.True(executor.Execute("basic_attack", "A").Retryable);
        Assert.Equal(new[] { "Turn Up" }, history);
    }

    [Fact]
    public void FailedInitialTurnNeverReleasesAnOffensiveKey()
    {
        var history = new List<string>();
        var inputs = new RecordingInputs(history) { RejectTurn = true };
        var frame = Frame(0);
        var preparation = new ComboFacingCoordinator(inputs, new SimulatedClock(0), () => frame, () => true);
        var rejected = new PreparedExecutor(preparation, inputs).Execute("basic_attack", "A");
        Assert.False(rejected.Accepted);
        Assert.False(rejected.Retryable);
        Assert.Equal(new[] { "Turn Up" }, history);
    }

    [Fact]
    public void TargetChangeWhileAwaitingTurnCannotReleaseOffensiveKey()
    {
        var clock = new SimulatedClock(0);
        var history = new List<string>();
        var frame = Frame(0);
        var inputs = new RecordingInputs(history);
        var executor = new PreparedExecutor(new ComboFacingCoordinator(inputs, clock, () => frame, () => true), inputs);
        Assert.True(executor.Execute("basic_attack", "A").Retryable);
        clock.Advance(10);
        frame = Frame(10, Direction.Up) with { Snapshot = Frame(10, Direction.Up).Snapshot with
            { Target = new TargetInfo { Id = "other", IsTargeted = true, PositionY = -5 } } };
        Assert.False(executor.Execute("basic_attack", "A").Accepted);
        Assert.Equal(new[] { "Turn Up" }, history);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("dead")]
    [InlineData("missing")]
    public void InvalidObservationCannotReleaseTheNextKey(string invalid)
    {
        var history = new List<string>();
        LiveFrame? frame = invalid == "missing" ? null : Frame(0);
        if (invalid == "dead") frame = frame! with { Snapshot = frame.Snapshot with
        { Player = frame.Snapshot.Player with { IsAlive = false } } };
        var clock = new SimulatedClock(invalid == "stale" ? 501 : 0);
        var executor = new PreparedExecutor(new ComboFacingCoordinator(new RecordingInputs(history), clock,
            () => frame, () => true), new RecordingInputs(history));
        Assert.False(executor.Execute("basic_attack", "A").Accepted);
        Assert.Empty(history);
    }

    [Fact]
    public void AlignmentWaitIsBoundedByTheExistingComboTimeout()
    {
        var clock = new SimulatedClock(0);
        var history = new List<string>();
        LiveFrame? latest = null;
        var inputs = new RecordingInputs(history);
        var executor = new PreparedExecutor(new ComboFacingCoordinator(inputs, clock, () => latest, () => true), inputs);
        var engine = new EarlyBuffPunishEngine(executor, clock, new EarlyPunishOptions { FirstToSecondMs = 0, SecondToThirdMs = 200 });
        for (var at = 0; at <= 1700; at += 50)
        {
            clock.Advance(at - clock.NowMs);
            latest = Frame(at, Direction.Down, active: at == 0); // game never confirms facing
            engine.Update(latest, true, true, true);
        }
        Assert.Equal(EarlyPunishState.Cancelled, engine.State);
        Assert.DoesNotContain("A", history);
        Assert.DoesNotContain("Q", history);
        Assert.DoesNotContain("S", history);
    }
}
