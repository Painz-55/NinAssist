using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Tests;

public sealed class NearbyPlayerMarkersTests
{
    private static LiveFrame Frame(int map = 190, long at = 100) => new(
        new GameSnapshot { Player = new PlayerState { Id = "7:local", PositionX = 10, PositionY = 20 } },
        at, map, "local", null, false, null, 0);

    [Fact]
    public void OnlyRecentNearbyClientRecordsBecomeVisualCandidates()
    {
        var list = new MapPlayersFrame(190, "7:local", 100, new[]
        {
            new MapPlayer(7, "local", 190, 10, 20, true, false),
            new MapPlayer(8, "near", 190, 11, 22, false, true),
            new MapPlayer(9, "far", 190, 50, 20, false, false),
            new MapPlayer(10, "unknown", 190, null, 20, false, false)
        }, 0);
        var marker = Assert.Single(NearbyPlayerMarkers.Select(list, Frame(), 200, radiusTiles: 12));
        Assert.Equal((8, 1d, 2d, true), (marker.Slot, marker.OffsetX, marker.OffsetY, marker.Selected));
        Assert.Equal(2, NearbyPlayerMarkers.Select(list, Frame(), 200).Count);
        Assert.Empty(NearbyPlayerMarkers.Select(list, Frame(), 1700));
        Assert.Empty(NearbyPlayerMarkers.Select(list, Frame(at: 500), 500));
        Assert.Empty(NearbyPlayerMarkers.Select(list, Frame(191), 200));
    }
}
