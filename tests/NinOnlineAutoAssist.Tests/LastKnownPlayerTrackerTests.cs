using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Tests;

public sealed class LastKnownPlayerTrackerTests
{
    private static LiveFrame Frame(long at, int map = 190) => new(
        new GameSnapshot { Player = new PlayerState { Id = "7:local", PositionX = 50, PositionY = 50 } },
        at, map, "local", null, false, null, 0);
    private static MapPlayersFrame List(long at, params MapPlayer[] players) => new(190, "7:local", at, players, 0)
    { GridWidth = 101, GridHeight = 101, RenderView = new(1600, 1600, 1088, 680) };

    [Fact]
    public void MissingRecordBecomesClearlyAgedLastKnownThenExpires()
    {
        var tracker = new LastKnownPlayerTracker();
        var player = new MapPlayer(34, "Kage", 190, 52, 53, false, false);
        var first = Assert.Single(tracker.Update(List(100, player), Frame(100), 100));
        Assert.True(first.CurrentlyReported);
        var last = Assert.Single(tracker.Update(List(200), Frame(200), 200));
        Assert.False(last.CurrentlyReported);
        Assert.Equal(100, last.ObservationAgeMs);
        Assert.Equal((2d, 3d), (last.OffsetX, last.OffsetY));
        Assert.Empty(tracker.Update(List(7_200), Frame(7_200), 7_200));
    }

    [Fact]
    public void SlotReuseAndMapChangesNeverInheritOldPosition()
    {
        var tracker = new LastKnownPlayerTracker();
        tracker.Update(List(100, new MapPlayer(34, "Kage", 190, 52, 53, false, false)), Frame(100), 100);
        var replacement = Assert.Single(tracker.Update(List(200, new MapPlayer(34, "Other", 190, 54, 55, false, false)), Frame(200), 200));
        Assert.Equal("Other", replacement.Name);
        Assert.Equal(0, replacement.MotionAgeMs);
        Assert.Empty(tracker.Update(List(300), Frame(300, 191), 300));
    }

    [Fact]
    public void ARepeatedCoordinateIsNotFreshMovement()
    {
        var tracker = new LastKnownPlayerTracker();
        var player = new MapPlayer(34, "Kage", 190, 52, 53, false, false);
        tracker.Update(List(100, player), Frame(100), 100);
        var unchanged = Assert.Single(tracker.Update(List(1200, player), Frame(1200), 1200));
        Assert.Equal(1100, unchanged.MotionAgeMs);
        var moved = Assert.Single(tracker.Update(List(1300, player with { X = 53 }), Frame(1300), 1300));
        Assert.Equal(0, moved.MotionAgeMs);
    }

    [Fact]
    public void TemporaryMissingCensusDoesNotEraseCacheOrDrawWithOldView()
    {
        var tracker = new LastKnownPlayerTracker();
        tracker.Update(List(100, new MapPlayer(34, "Kage", 190, 52, 53, false, false)), Frame(100), 100);
        Assert.Empty(tracker.Update(null, Frame(200), 200));
        var last = Assert.Single(tracker.Update(List(300), Frame(300), 300));
        Assert.False(last.CurrentlyReported);
        Assert.Equal(200, last.ObservationAgeMs);
    }

    [Fact]
    public void MapVillageFiltersApplyToFreshAndLastKnownOverlayMarkers()
    {
        var tracker = new LastKnownPlayerTracker();
        var local = new MapPlayer(7, "local", 190, 50, 50, true, false) { Village = PlayerVillage.Leaf };
        var ally = new MapPlayer(34, "ally", 190, 52, 53, false, false) { Village = PlayerVillage.Leaf };
        var enemy = new MapPlayer(35, "enemy", 190, 53, 53, false, false) { Village = PlayerVillage.Sand };
        var unknown = new MapPlayer(36, "unknown", 190, 54, 53, false, false);
        tracker.Update(List(100, local, ally, enemy, unknown), Frame(100), 100);

        var onlyEnemy = Assert.Single(tracker.Update(List(200, local), Frame(200), 200,
            hideOwnVillage: true, hiddenVillages: [PlayerVillage.Mist]));
        Assert.Equal("enemy", onlyEnemy.Name);
        Assert.False(onlyEnemy.CurrentlyReported);

        var hiddenEnemy = tracker.Update(List(300, local), Frame(300), 300,
            hideOwnVillage: true, hiddenVillages: [PlayerVillage.Sand]);
        Assert.Empty(hiddenEnemy);
        Assert.Equal(3, tracker.Update(List(400, local), Frame(400), 400).Count);
    }

    [Fact]
    public void UnknownLocalVillageFailsClosedAndCacheNeverReusesOldAffiliation()
    {
        var tracker = new LastKnownPlayerTracker();
        var local = new MapPlayer(7, "local", 190, 50, 50, true, false) { Village = PlayerVillage.Leaf };
        var player = new MapPlayer(34, "enemy", 190, 52, 53, false, false) { Village = PlayerVillage.Sand };
        tracker.Update(List(100, local, player), Frame(100), 100);
        Assert.Empty(tracker.Update(List(200, local with { Village = PlayerVillage.Unknown }),
            Frame(200), 200, hideOwnVillage: true));
        Assert.Empty(tracker.Update(List(300, local, player with { Village = PlayerVillage.Unknown }),
            Frame(300), 300, hideOwnVillage: true));
        Assert.Single(tracker.Update(List(400, local), Frame(400), 400)); // Unfiltered cache is intact.
    }
}
