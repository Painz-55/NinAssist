using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Simulation;

namespace NinOnlineAutoAssist.Tests;

public sealed class DiagonalFollowTests
{
    private static NavigationFrame Grid(params (int X, int Y, NavigationCell Cell)[] changes)
    {
        var cells = Enumerable.Repeat(NavigationCell.Open, 121).ToArray();
        foreach (var (x, y, cell) in changes) cells[y * 11 + x] = cell;
        return new(51, 0, 0, 0, 11, 11, cells, true, changes.Count(c => c.Cell == NavigationCell.Hazard));
    }
    private static LiveFrame Frame(double tx, double ty) => new(new GameSnapshot
    {
        Player = new PlayerState { Id = "local", PositionX = 5.2, PositionY = 5.2 },
        Target = new TargetInfo { Id = "target", IsTargeted = true, PositionX = tx, PositionY = ty }
    }, 0, 51, "Local", "Target", false, null, 0);

    [Theory]
    [InlineData(8.2, 2.2, Direction.Up, Direction.Right, 6, 4)]
    [InlineData(2.2, 2.2, Direction.Up, Direction.Left, 4, 4)]
    [InlineData(8.2, 8.2, Direction.Down, Direction.Right, 6, 6)]
    [InlineData(2.2, 8.2, Direction.Down, Direction.Left, 4, 6)]
    public void OpenRoutesUseAllFourDiagonals(double tx, double ty, Direction primary, Direction secondary, int x, int y)
    {
        var step = FollowPathPlanner.Next(Frame(tx, ty), Grid(), 1);
        Assert.NotNull(step);
        Assert.Equal(primary, step.Direction); Assert.Equal(secondary, step.SecondaryDirection);
        Assert.Equal(new GridPoint(x, y), step.Destination);
        Assert.True(Grid().SafeStep(new(5, 5), step));
    }

    [Theory]
    [InlineData(6, 5, NavigationCell.Blocked)]
    [InlineData(5, 4, NavigationCell.Blocked)]
    [InlineData(6, 5, NavigationCell.Unknown)]
    [InlineData(5, 4, NavigationCell.Unknown)]
    [InlineData(6, 4, NavigationCell.Blocked)]
    [InlineData(6, 4, NavigationCell.Unknown)]
    [InlineData(7, 5, NavigationCell.Hazard)]
    public void DiagonalRejectsUnsafeDestinationOrEitherSweptSide(int x, int y, NavigationCell cell)
    {
        var grid = Grid((x, y, cell));
        Assert.False(grid.SafeStep(new(5, 5), new(Direction.Up, new(6, 4), Direction.Right)));
        var alternative = FollowPathPlanner.Next(Frame(8.2, 2.2), grid, 1);
        if (alternative is not null)
        {
            Assert.True(grid.SafeStep(new(5, 5), alternative));
            Assert.NotEqual(new GridPoint(6, 4), alternative.Destination);
        }
    }

    [Theory]
    [InlineData(Direction.Up, Direction.Down)]
    [InlineData(Direction.Left, Direction.Right)]
    [InlineData(Direction.Up, Direction.Up)]
    [InlineData(Direction.None, Direction.Right)]
    public void ContradictoryOrDuplicateComponentsAreNeverDispatched(Direction first, Direction second)
    {
        var count = 0;
        using var held = new HeldDirectionController(_ => false, _ => { count++; return true; }, _ => true);
        Assert.False(held.Set(first, true, second));
        Assert.False(Grid().SafeStep(new(5, 5), new(first, new(6, 4), second)));
        Assert.Equal(0, count);
    }

    [Fact]
    public void TurnsRetainSharedKeysWithoutPulsesOrThreeKeyOverlaps()
    {
        var events = new List<string>();
        var physical = new HashSet<Direction>();
        using var held = new HeldDirectionController(physical.Contains,
            d => { Assert.True(physical.Add(d)); Assert.True(physical.Count <= 2); events.Add($"down:{d}"); return true; },
            d => { physical.Remove(d); events.Add($"up:{d}"); return true; });
        Assert.True(held.Set(Direction.Up, true, Direction.Right));
        for (var i = 0; i < 50; i++) Assert.True(held.Set(Direction.Up, true, Direction.Right));
        Assert.True(held.Set(Direction.Right, true));
        Assert.Equal(new[] { "down:Up", "down:Right", "up:Up" }, events);
        Assert.True(held.Set(Direction.Down, true, Direction.Right));
        Assert.True(held.Set(Direction.Down, true, Direction.Left));
        Assert.Equal(new[] { "down:Up", "down:Right", "up:Up", "down:Down", "up:Right", "down:Left" }, events);
        held.Release(); Assert.Empty(physical);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialKeyDownFailureOrExceptionReleasesTheFirstComponent(bool throws)
    {
        var physical = new HashSet<Direction>();
        using var held = new HeldDirectionController(physical.Contains, d =>
        {
            if (d == Direction.Right)
            {
                if (throws) throw new InvalidOperationException("second key");
                return false;
            }
            physical.Add(d); return true;
        }, d => { physical.Remove(d); return true; });
        if (throws) Assert.Throws<InvalidOperationException>(() => held.Set(Direction.Up, true, Direction.Right));
        else Assert.False(held.Set(Direction.Up, true, Direction.Right));
        Assert.Empty(physical);
    }

    [Fact]
    public void ManualComponentIsNeverTakenOrReleased()
    {
        var events = new List<string>();
        using var held = new HeldDirectionController(d => d == Direction.Right,
            d => { events.Add($"down:{d}"); return true; }, d => { events.Add($"up:{d}"); return true; });
        Assert.False(held.Set(Direction.Up, true, Direction.Right));
        Assert.Empty(events);
    }

    [Fact]
    public void SafetyRevocationAndDisposalReleaseBothComponents()
    {
        var physical = new HashSet<Direction>();
        var held = new HeldDirectionController(physical.Contains, d => physical.Add(d), d => physical.Remove(d));
        held.Set(Direction.Down, true, Direction.Left);
        held.Set(Direction.Down, false, Direction.Left);
        Assert.Empty(physical);
        held.Set(Direction.Up, true, Direction.Right);
        held.Dispose(); Assert.Empty(physical);
    }

    [Fact]
    public void FollowEngineMaintainsBothAxesAcrossTilesAndReleasesBothOnStaleData()
    {
        var clock = new SimulatedClock(0);
        var physical = new HashSet<Direction>(); var downs = 0;
        using var held = new HeldDirectionController(physical.Contains,
            d => { downs++; return physical.Add(d); }, d => physical.Remove(d));
        var follow = new AutoFollowEngine(clock, new CombatAssistSettings(), step =>
            held.Set(step.Direction, true, step.SecondaryDirection) ? ActionResult.Ok() : ActionResult.Rejected("blocked"), held.Release);
        for (var i = 0; i < 3; i++)
        {
            var frame = Frame(9.2, 1.2);
            frame = frame with { CapturedAtMs = clock.NowMs, Snapshot = frame.Snapshot with { Player = frame.Snapshot.Player with
                { PositionX = 5.2 + i, PositionY = 5.2 - i } } };
            follow.Update(frame, Grid() with { CapturedAtMs = clock.NowMs }, true, false);
            Assert.True(physical.SetEquals(new[] { Direction.Up, Direction.Right }));
            clock.Advance(20);
        }
        Assert.Equal(2, downs);
        clock.Advance(1000);
        follow.Update(Frame(9.2, 1.2), Grid(), true, false);
        Assert.Empty(physical);
    }
}
