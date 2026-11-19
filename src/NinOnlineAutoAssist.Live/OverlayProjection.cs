namespace NinOnlineAutoAssist.Live;

/// <summary>Visual-only world-to-client projection using the observed game render view.
/// An unknown view fails closed rather than assuming the local player is centered.</summary>
public static class OverlayProjection
{
    private const double PixelsPerTile = 32;

    // 2026-11-18, map 190: a screenshot corroborated the local sprite at screen center and
    // a second player's world offset, while the only non-null View kept a stale center.
    // This visual-only calibration must not be extended to unobserved maps or used for input.
    public static RenderViewObservation? CalibratedMap190(double localX, double localY,
        int gridWidth, int gridHeight, double sizeX, double sizeY)
    {
        if (gridWidth != 101 || gridHeight != 101 || !double.IsFinite(localX) || !double.IsFinite(localY) ||
            localX < 0 || localX >= gridWidth || localY < 0 || localY >= gridHeight ||
            !double.IsFinite(sizeX) || !double.IsFinite(sizeY) || sizeX is < 320 or > 8000 ||
            sizeY is < 240 or > 8000 || sizeX > gridWidth * PixelsPerTile ||
            sizeY > gridHeight * PixelsPerTile) return null;
        return new(Math.Clamp(localX * PixelsPerTile, sizeX / 2, gridWidth * PixelsPerTile - sizeX / 2),
            Math.Clamp(localY * PixelsPerTile, sizeY / 2, gridHeight * PixelsPerTile - sizeY / 2), sizeX, sizeY);
    }

    public static (double X, double Y)? Project(NearbyPlayerMarkers.Marker marker, LiveFrame? frame,
        MapPlayersFrame? map, double clientWidth, double clientHeight)
    {
        if (frame is null || map is null || map.Map != frame.Map || map.LocalId != frame.Snapshot.Player.Id ||
            map.RenderView is not { } view || !double.IsFinite(view.CenterX) || !double.IsFinite(view.CenterY) ||
            !double.IsFinite(view.SizeX) || !double.IsFinite(view.SizeY) ||
            view.SizeX is < 320 or > 8000 || view.SizeY is < 240 or > 8000 ||
            map.GridWidth is not > 0 or > 512 || map.GridHeight is not > 0 or > 512 ||
            !double.IsFinite(clientWidth) || !double.IsFinite(clientHeight) ||
            clientWidth is < 320 or > 8000 || clientHeight is < 240 or > 8000) return null;
        var localX = frame.Snapshot.Player.PositionX;
        var localY = frame.Snapshot.Player.PositionY;
        var worldX = localX + marker.OffsetX;
        var worldY = localY + marker.OffsetY;
        if (!double.IsFinite(worldX) || !double.IsFinite(worldY) ||
            localX < 0 || localY < 0 || worldX < 0 || worldY < 0 ||
            worldX >= map.GridWidth || worldY >= map.GridHeight) return null;
        var x = (worldX * PixelsPerTile - view.CenterX + view.SizeX / 2) * clientWidth / view.SizeX;
        var y = (worldY * PixelsPerTile - view.CenterY + view.SizeY / 2) * clientHeight / view.SizeY;
        return x is >= 0 && y is >= 0 && x < clientWidth && y < clientHeight ? (x, y) : null;
    }
}
