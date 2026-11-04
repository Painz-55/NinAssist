using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Simulation;

namespace NinOnlineAutoAssist.Tests;

public sealed class FollowSteeringTests
{
    private static NavigationFrame Grid(params (int X, int Y, NavigationCell Cell)[] changes)
    {
        var cells = Enumerable.Repeat(NavigationCell.Open, 225).ToArray();
        foreach (var (x, y, cell) in changes) cells[y * 15 + x] = cell;
        return new(51, 0, 0, 0, 15, 15, cells, true, changes.Count(c => c.Cell == NavigationCell.Hazard));
    }
    private static LiveFrame Frame(double x, double y, double tx, double ty, long at = 0) => new(new GameSnapshot
    {
        Player = new PlayerState { Id = "local", PositionX = x, PositionY = y },
        Target = new TargetInfo { Id = "target", IsTargeted = true, PositionX = tx, PositionY = ty }
    }, at, 51, "Local", "Target", false, null, 0);

    [Fact]
    public void SubTilePositionsKeepTheDiagonalAcrossUnequalGridCrossingsNearTheTarget()
    {
        FollowStep? previous = null;
        foreach (var (x, y) in new[] { (5.90, 6.05), (6.02, 5.93), (6.12, 5.83) })
        {
            var next = FollowPathPlanner.Next(Frame(x, y, 7.1, 4.85), Grid(), 0.75, previous);
            Assert.NotNull(next);
            Assert.Equal(Direction.Up, next.Direction);
            Assert.Equal(Direction.Right, next.SecondaryDirection);
            previous = next;
        }
    }

    [Fact]
    public void AngularHysteresisAvoidsCardinalDiagonalChatterWithoutDelayingRealTurns()
    {
        var start = new GridPoint(2, 2);
        var previous = FollowSteering.Heading(10, 4.3, start);
        Assert.Equal(Direction.Right, previous.SecondaryDirection);
        foreach (var minor in new[] { 4.0, 3.8, 4.4, 3.1 })
        {
            previous = FollowSteering.Heading(10, minor, start, previous);
            Assert.Equal(Direction.Down, previous.Direction);
            Assert.Equal(Direction.Right, previous.SecondaryDirection);
        }
        previous = FollowSteering.Heading(10, 2.9, start, previous);
        Assert.Equal(Direction.Right, previous.Direction); Assert.Equal(Direction.None, previous.SecondaryDirection);
        previous = FollowSteering.Heading(10, 4.8, start, previous);
        Assert.Equal(Direction.None, previous.SecondaryDirection);
        previous = FollowSteering.Heading(10, 5.1, start, previous);
        Assert.Equal(Direction.Right, previous.SecondaryDirection);
        previous = FollowSteering.Heading(-10, -10, start, previous);
        Assert.Equal(Direction.Up, previous.Direction); Assert.Equal(Direction.Left, previous.SecondaryDirection);
    }

    [Theory]
    [InlineData(NavigationCell.Blocked)]
    [InlineData(NavigationCell.Unknown)]
    [InlineData(NavigationCell.Hazard)]
    public void DirectRouteRejectsBlockedUnknownOrHazardousCells(NavigationCell cell)
    {
        Assert.False(FollowSteering.ClearSegment(Grid((5, 5, cell)), 2.2, 5.2, 9.2, 5.2));
    }

    [Theory]
    [InlineData(3, 2)]
    [InlineData(2, 3)]
    public void ExactCornerCrossingsCheckBothSidesNotJustTheDiagonalDestination(int x, int y)
    {
        Assert.False(FollowSteering.ClearSegment(Grid((x, y, NavigationCell.Blocked)), 2.5, 2.5, 5.5, 5.5));
    }

    [Fact]
    public void StraightSegmentsHandleReverseAxesSubTileEndpointsAndBoundaries()
    {
        var grid = Grid();
        Assert.True(FollowSteering.ClearSegment(grid, 9, 5, 2.2, 5));
        Assert.True(FollowSteering.ClearSegment(grid, 5, 9, 5, 2.2));
        Assert.True(FollowSteering.ClearSegment(grid, 5.1, 5.1, 5.2, 5.2));
        Assert.False(FollowSteering.ClearSegment(grid, 5, 5, 100, 100));
        Assert.False(FollowSteering.ClearSegment(grid, double.NaN, 5, 6, 6));
        Assert.False(FollowSteering.ClearSegment(grid with { DynamicHazardsKnown = false }, 5, 5, 6, 6));
        Assert.Null(FollowSteering.Heading(0, 0, new(5, 5)).Offset);
        Assert.Null(FollowSteering.Heading(double.NaN, 1, new(5, 5)).Offset);
    }

    [Fact]
    public void RealDistanceStopsPreciselyAndDoesNotWanderToAnExtraGridCell()
    {
        Assert.Null(FollowPathPlanner.Next(Frame(5.2, 5.2, 5.95, 5.2), Grid(), 0.75));
        var closer = FollowPathPlanner.Next(Frame(5.2, 5.2, 6.0, 5.2), Grid(), 0.75);
        Assert.NotNull(closer); Assert.Equal(Direction.Right, closer.Direction);
    }

    [Fact]
    public void ANewObstacleOverridesThePreviousDirectHeadingImmediately()
    {
        var frame = Frame(2.2, 5.2, 9.2, 5.2);
        var old = FollowPathPlanner.Next(frame, Grid(), 0.75)!;
        var grid = Grid((3, 5, NavigationCell.Blocked));
        var changed = FollowPathPlanner.Next(frame, grid, 0.75, old);
        Assert.NotNull(changed);
        Assert.NotEqual(old.Destination, changed.Destination);
        Assert.True(grid.SafeStep(new(2, 5), changed));
    }

    [Fact]
    public void HeldPursuitTracksFractionalMotionAndStopsAtTheConfiguredCloserDistance()
    {
        var clock = new SimulatedClock(0); var physical = new HashSet<Direction>(); var downs = 0;
        using var held = new HeldDirectionController(physical.Contains,
            d => { downs++; return physical.Add(d); }, d => physical.Remove(d));
        var engine = new AutoFollowEngine(clock, new CombatAssistSettings { FollowStopDistance = 0.75 }, step =>
            held.Set(step.Direction, true, step.SecondaryDirection) ? ActionResult.Ok() : ActionResult.Rejected("blocked"), held.Release);
        foreach (var (x, y) in new[] { (5.90, 6.05), (6.02, 5.93), (6.12, 5.83) })
        {
            engine.Update(Frame(x, y, 7.1, 4.85, clock.NowMs), Grid() with { CapturedAtMs = clock.NowMs }, true, false);
            Assert.True(physical.SetEquals(new[] { Direction.Up, Direction.Right }));
            clock.Advance(20);
        }
        Assert.Equal(2, downs);
        engine.Update(Frame(6.60, 5.35, 7.1, 4.85, clock.NowMs), Grid() with { CapturedAtMs = clock.NowMs }, true, false);
        Assert.Empty(physical);
    }
}
