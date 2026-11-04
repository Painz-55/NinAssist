using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Simulation;

namespace NinOnlineAutoAssist.Tests;

public sealed class AutoFollowTests
{
    private static LiveFrame Frame(long at = 0, double x = 1.2, double y = 4.2, double tx = 7.2, double ty = 4.2) => new(
        new GameSnapshot { Player = new PlayerState { Id = "local", PositionX = x, PositionY = y },
            Target = new TargetInfo { Id = "target", IsTargeted = true, PositionX = tx, PositionY = ty } },
        at, 51, "Player", "Target", false, null, 0);
    private static NavigationFrame Grid(long time = 0, params (int X, int Y, NavigationCell Cell)[] special)
    {
        var cells = Enumerable.Repeat(NavigationCell.Open, 81).ToArray();
        foreach (var (x, y, cell) in special) cells[y * 9 + x] = cell;
        return new(51, time, 0, 0, 9, 9, cells, true, special.Count(s => s.Cell == NavigationCell.Hazard));
    }

    [Fact]
    public void OpenGroundFollowsTheTargetWithCardinalSteps()
    {
        var next = FollowPathPlanner.Next(Frame(), Grid(), 1);
        Assert.NotNull(next);
        Assert.Equal(Direction.Right, next.Direction);
        Assert.Equal(new GridPoint(2, 4), next.Destination);
    }

    [Fact]
    public void PlannerDetoursAroundATrapAndItsMargin()
    {
        var grid = Grid(0, (4, 4, NavigationCell.Hazard));
        var frame = Frame();
        var visited = new List<GridPoint>();
        for (var i = 0; i < 30; i++)
        {
            var next = FollowPathPlanner.Next(frame, grid, 1);
            if (next is null) break;
            Assert.True(grid.Safe(next.Destination));
            visited.Add(next.Destination);
            frame = frame with { Snapshot = frame.Snapshot with { Player = frame.Snapshot.Player with
            { PositionX = next.Destination.X + 0.2, PositionY = next.Destination.Y + 0.2 } } };
            if (Math.Pow(frame.Snapshot.Player.PositionX - 7.2, 2) + Math.Pow(frame.Snapshot.Player.PositionY - 4.2, 2) <= 1) break;
        }
        Assert.NotEmpty(visited);
        Assert.Contains(visited, p => p.Y <= 2 || p.Y >= 6);
        Assert.True(Math.Pow(frame.Snapshot.Player.PositionX - 7.2, 2) + Math.Pow(frame.Snapshot.Player.PositionY - 4.2, 2) <= 1);
    }

    [Theory]
    [InlineData(NavigationCell.Unknown)]
    [InlineData(NavigationCell.Blocked)]
    [InlineData(NavigationCell.Hazard)]
    public void ImpassableBarrierNeverProducesATrespassingStep(NavigationCell cell)
    {
        var barriers = Enumerable.Range(0, 9).Select(y => (4, y, cell)).ToArray();
        Assert.Null(FollowPathPlanner.Next(Frame(), Grid(0, barriers), 1));
    }

    [Fact]
    public void IncompleteOrWrongMapNavigationCannotBeUsed()
    {
        Assert.Null(FollowPathPlanner.Next(Frame(), Grid() with { DynamicHazardsKnown = false }, 1));
        Assert.Null(FollowPathPlanner.Next(Frame(), Grid() with { Map = 99 }, 1));
        Assert.Equal(NavigationCell.Unknown, (Grid() with { Cells = Array.Empty<NavigationCell>() }).Cell(2, 2));
        Assert.False(Grid().SafeStep(new GridPoint(1, 4), new FollowStep(Direction.Right, new GridPoint(3, 4))));
        Assert.False(Grid().SafeStep(new GridPoint(1, 4), new FollowStep(Direction.None, new GridPoint(1, 4))));
    }

    [Theory]
    [InlineData("released")]
    [InlineData("stale-frame")]
    [InlineData("stale-grid")]
    [InlineData("unknown-grid")]
    [InlineData("priority")]
    [InlineData("in-range")]
    [InlineData("missing-grid")]
    public void FollowStopsWithoutSendingAnotherMovementInput(string block)
    {
        var clock = new SimulatedClock(0);
        var calls = 0;
        var engine = new AutoFollowEngine(clock, new CombatAssistSettings(), _ => { calls++; return ActionResult.Ok(); });
        var frame = Frame(block == "stale-frame" ? -501 : 0, tx: block == "in-range" ? 1.5 : 7.2);
        NavigationFrame? grid = Grid(block == "stale-grid" ? -501 : 0);
        if (block == "unknown-grid") grid = grid with { DynamicHazardsKnown = false };
        if (block == "missing-grid") grid = null;
        engine.Update(frame, grid, block != "released", block == "priority");
        Assert.Equal(0, calls);
    }

    [Fact]
    public void AStuckCharacterStopsUntilHoldIsReleased()
    {
        var clock = new SimulatedClock(0);
        var calls = 0;
        var engine = new AutoFollowEngine(clock, new CombatAssistSettings { FollowStepIntervalMs = 100 }, _ => { calls++; return ActionResult.Ok(); });
        for (var at = 0; at <= 2000; at += 100)
        { clock.Advance(at - clock.NowMs); engine.Update(Frame(at), Grid(at), true, false); }
        var stoppedAt = calls;
        clock.Advance(100); engine.Update(Frame(2100), Grid(2100), true, false);
        Assert.Equal(stoppedAt, calls);
        engine.Update(Frame(2100), Grid(2100), false, false);
        clock.Advance(100); engine.Update(Frame(2200), Grid(2200), true, false);
        Assert.Equal(stoppedAt + 1, calls);
    }

    [Fact]
    public void ReplanningUsesNewHazardsInsteadOfReplayingTheOldRoute()
    {
        var first = FollowPathPlanner.Next(Frame(), Grid(), 1)!;
        var changed = Grid(0, (first.Destination.X, first.Destination.Y, NavigationCell.Blocked));
        Assert.False(changed.SafeStep(new GridPoint(1, 4), first));
        var second = FollowPathPlanner.Next(Frame(), changed, 1);
        Assert.NotNull(second);
        Assert.NotEqual(first.Destination, second.Destination);
    }
}
