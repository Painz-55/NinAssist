using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Simulation;

namespace NinOnlineAutoAssist.Tests;

public sealed class HeldFollowTests
{
    [Fact]
    public void SameDirectionRenewalsNeverPulseAndDirectionChangeReleasesFirst()
    {
        var events = new List<string>();
        using var held = new HeldDirectionController(_ => false, d => { events.Add($"down:{d}"); return true; },
            d => { events.Add($"up:{d}"); return true; });
        for (var i = 0; i < 100; i++) Assert.True(held.Set(Direction.Right, true));
        Assert.Equal(new[] { "down:Right" }, events);
        held.Set(Direction.Up, true); held.Set(Direction.Up, false); held.Release();
        Assert.Equal(new[] { "down:Right", "up:Right", "down:Up", "up:Up" }, events);
    }

    [Fact]
    public void ManualKeysAreNeverAcquiredOrReleased()
    {
        var events = new List<string>();
        using var held = new HeldDirectionController(_ => true, d => { events.Add("down"); return true; },
            d => { events.Add("up"); return true; });
        Assert.False(held.Set(Direction.Right, true)); held.Release();
        Assert.Empty(events);
    }

    [Fact]
    public void FailedKeyUpBlocksNewKeyDownAndDisposalRetriesOnlyOwnedKey()
    {
        var downs = 0; var ups = 0;
        var held = new HeldDirectionController(_ => false, _ => { downs++; return true; }, _ => ++ups >= 2);
        held.Set(Direction.Left, true);
        Assert.False(held.Set(Direction.Down, true)); Assert.True(held.Faulted);
        held.Dispose();
        Assert.Equal(1, downs); Assert.Equal(2, ups); Assert.Equal(Direction.None, held.Held);
    }

    [Fact]
    public void FailedKeyDownDoesNotCreateOwnership()
    {
        var ups = 0;
        using var held = new HeldDirectionController(_ => false, _ => false, _ => { ups++; return true; });
        Assert.False(held.Set(Direction.Left, true)); held.Release(); Assert.Equal(0, ups);
    }

    private static LiveFrame Frame(long at, double x = 1.2) => new(new GameSnapshot
    {
        Player = new PlayerState { Id = "local", PositionX = x, PositionY = 4.2 },
        Target = new TargetInfo { Id = "target", IsTargeted = true, PositionX = 7.2, PositionY = 4.2 }
    }, at, 51, "Local", "Target", false, null, 0);
    private static NavigationFrame Grid(long at) => new(51, at, 0, 0, 9, 9,
        Enumerable.Repeat(NavigationCell.Open, 81).ToArray(), true, 0);

    [Fact]
    public void CrossingSafeTilesKeepsOneContinuousKeyDownEvenInsideLegacyInterval()
    {
        var events = new List<string>();
        using var held = new HeldDirectionController(_ => false, d => { events.Add($"down:{d}"); return true; },
            d => { events.Add($"up:{d}"); return true; });
        var clock = new SimulatedClock(0);
        var follow = new AutoFollowEngine(clock, new CombatAssistSettings(), s =>
            held.Set(s.Direction, true) ? ActionResult.Ok() : ActionResult.Rejected("blocked"), held.Release);
        for (var i = 0; i < 4; i++)
        {
            clock.Advance(i == 0 ? 0 : 20);
            follow.Update(Frame(clock.NowMs, 1.2 + i), Grid(clock.NowMs), true, false);
        }
        Assert.Equal(new[] { "down:Right" }, events);
        follow.Update(Frame(clock.NowMs, 6.5), Grid(clock.NowMs), true, false);
        Assert.Equal(new[] { "down:Right", "up:Right" }, events);
    }

    [Theory]
    [InlineData("released")]
    [InlineData("stale")]
    [InlineData("grid")]
    [InlineData("hazards")]
    [InlineData("target")]
    [InlineData("priority")]
    [InlineData("range")]
    [InlineData("reset")]
    [InlineData("exception")]
    public void StopPathsReleaseAnAlreadyHeldKey(string reason)
    {
        var clock = new SimulatedClock(0); var down = false; var throws = false;
        using var held = new HeldDirectionController(_ => false, _ => { down = true; return true; }, _ => { down = false; return true; });
        var follow = new AutoFollowEngine(clock, new CombatAssistSettings(), s =>
        {
            if (throws) throw new InvalidOperationException("test");
            return held.Set(s.Direction, true) ? ActionResult.Ok() : ActionResult.Rejected("blocked");
        }, held.Release);
        follow.Update(Frame(0), Grid(0), true, false); Assert.True(down);
        clock.Advance(50);
        var frame = Frame(50, reason == "range" ? 6.5 : 1.2);
        NavigationFrame? grid = Grid(50);
        if (reason == "stale") frame = Frame(-501);
        if (reason == "grid") grid = null;
        if (reason == "hazards") grid = grid! with { DynamicHazardsKnown = false };
        if (reason == "target") frame = frame with { Snapshot = frame.Snapshot with { Target = null } };
        if (reason == "reset") follow.Reset();
        else if (reason == "exception")
        { throws = true; Assert.Throws<InvalidOperationException>(() => follow.Update(frame, grid, true, false)); }
        else follow.Update(frame, grid, reason != "released", reason == "priority");
        Assert.False(down);
    }
}
