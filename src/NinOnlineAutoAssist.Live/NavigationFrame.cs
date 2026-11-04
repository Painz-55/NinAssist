namespace NinOnlineAutoAssist.Live;

public enum NavigationCell { Unknown, Open, Blocked, Hazard }
public readonly record struct GridPoint(int X, int Y);

/// <summary>Immutable, bounded region of freshly observed terrain and visible dynamic hazards.</summary>
public sealed record NavigationFrame(int Map, long CapturedAtMs, int OriginX, int OriginY,
    int Width, int Height, IReadOnlyList<NavigationCell> Cells, bool DynamicHazardsKnown, int VisibleHazards)
{
    public int IgnoredOwnShots { get; init; }
    public NavigationCell Cell(int x, int y) => Width <= 0 || Height <= 0 || Cells.Count != (long)Width * Height ||
        x < OriginX || y < OriginY || x >= OriginX + Width || y >= OriginY + Height
        ? NavigationCell.Unknown : Cells[(y - OriginY) * Width + x - OriginX];

    public bool Safe(GridPoint point)
    {
        if (Cell(point.X, point.Y) != NavigationCell.Open) return false;
        // One-tile buffer around mapped hazards/projectiles, including diagonal approaches.
        for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
                if (Cell(point.X + dx, point.Y + dy) == NavigationCell.Hazard) return false;
        return true;
    }

    public bool SafeStep(GridPoint start, FollowStep step)
    {
        if (step.Offset is not { } offset) return false;
        var expected = new GridPoint(start.X + offset.X, start.Y + offset.Y);
        if (step.Destination != expected || !Safe(expected)) return false;
        // Diagonal swept path must not clip an obstacle/unknown/hazard on either side of the corner.
        return offset.X == 0 || offset.Y == 0 ||
            (Safe(new(start.X + offset.X, start.Y)) && Safe(new(start.X, start.Y + offset.Y)));
    }
}
