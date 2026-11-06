using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Tests;

public sealed class ObservedCastIdsTests
{
    private static LiveFrame Frame(long at, int spell = 235, long start = 1000,
        string player = "local", int map = 1) => new(new GameSnapshot
        { Player = new PlayerState { Id = player } }, at, map, "Local", null, false, null, 0)
        { Cast = new CastObservation(true, spell, start, 1000, 900) };

    [Fact]
    public void RequiresTwoDistinctFreshMatchingCastReadsWithoutEnablingInput()
    {
        var ids = new ObservedCastIds();
        ids.Observe(Frame(0), 0);
        ids.Observe(Frame(0), 0);
        Assert.Empty(ids.Recent);
        ids.Observe(Frame(50), 50);
        Assert.Equal([235], ids.Recent);
        ids.Observe(Frame(100, spell: 241), 100);
        Assert.Equal([235], ids.Recent);
        ids.Observe(Frame(150, spell: 241), 150);
        Assert.Equal([241, 235], ids.Recent);
    }

    [Fact]
    public void UnknownStaleInterruptedOrUnstableReadsDoNotProduceId()
    {
        var ids = new ObservedCastIds();
        ids.Observe(Frame(0), 0);
        ids.Observe(Frame(50) with { Cast = null }, 50);
        ids.Observe(Frame(100), 100);
        ids.Observe(Frame(150), 400);
        Assert.Empty(ids.Recent);
        ids.Observe(Frame(450), 450);
        ids.Observe(Frame(700), 700); // More than 200ms between samples.
        Assert.Empty(ids.Recent);
        ids.Observe(Frame(750, start: 2000), 750);
        Assert.Empty(ids.Recent);
        ids.Observe(Frame(800, start: 2000), 800);
        Assert.Equal([235], ids.Recent);
        ids.Reset();
        Assert.Empty(ids.Recent);
    }

    [Fact]
    public void PlayerOrMapChangeClearsHistoryAndDoesNotBorrowPriorCastSample()
    {
        var ids = new ObservedCastIds();
        ids.Observe(Frame(0), 0);
        ids.Observe(Frame(50), 50);
        ids.Observe(Frame(100, player: "other"), 100);
        Assert.Empty(ids.Recent);
        ids.Observe(Frame(150, player: "other"), 150);
        Assert.Equal([235], ids.Recent);
        ids.Observe(Frame(200, player: "other", map: 2), 200);
        Assert.Empty(ids.Recent);
    }
}
