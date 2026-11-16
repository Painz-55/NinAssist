using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Tests;

public sealed class OverlayProjectionTests
{
    private static LiveFrame Frame(double x = 50, double y = 50, int map = 190) => new(
        new GameSnapshot { Player = new PlayerState { Id = "7:local", PositionX = x, PositionY = y } },
        100, map, "local", null, false, null, 0);
    private static MapPlayersFrame Map(RenderViewObservation? view = null) => new(190, "7:local", 100, [], 0)
    { GridWidth = 101, GridHeight = 101, RenderView = view ?? new(1600, 1600, 1088, 680) };

    [Fact]
    public void UsesActualViewScaleAndCenterRatherThanAssumingThirtyTwoScreenPixelsPerTile()
    {
        var marker = new NearbyPlayerMarkers.Marker(34, "other", 2, -3, false);
        Assert.Equal((640 + 64d * 1280 / 1088, 400 - 96d * 800 / 680),
            OverlayProjection.Project(marker, Frame(), Map(), 1280, 800));
        var clampedAtEdge = Map(new RenderViewObservation(544, 544, 1088, 680));
        Assert.Equal((384d * 1280 / 1088, 400d),
            OverlayProjection.Project(marker, Frame(10, 20), clampedAtEdge, 1280, 800));
    }

    [Fact]
    public void UnknownOrInvalidViewNeverDraws()
    {
        var marker = new NearbyPlayerMarkers.Marker(34, "other", 2, -3, false);
        Assert.Null(OverlayProjection.Project(marker, Frame(), Map() with { RenderView = null }, 1280, 800));
        Assert.Null(OverlayProjection.Project(marker, Frame(map: 191), Map(), 1280, 800));
        Assert.Null(OverlayProjection.Project(marker with { OffsetX = 80 }, Frame(), Map(), 1280, 800));
    }
}
