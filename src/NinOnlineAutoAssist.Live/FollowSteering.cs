using NinOnlineAutoAssist.Core;

namespace NinOnlineAutoAssist.Live;

/// <summary>Continuous-position steering on clear ground. Never substitutes for obstacle pathfinding.</summary>
public static class FollowSteering
{
    public static FollowStep Heading(double dx, double dy, GridPoint start, FollowStep? previous = null)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) return new(Direction.None, start);
        var ax = Math.Abs(dx); var ay = Math.Abs(dy);
        var sx = ax < 0.10 ? 0 : Math.Sign(dx);
        var sy = ay < 0.10 ? 0 : Math.Sign(dy);
        if (sx == 0 && sy == 0) return new(Direction.None, start);
        if (sx != 0 && sy != 0)
        {
            var ratio = Math.Min(ax, ay) / Math.Max(ax, ay);
            var threshold = Math.Sqrt(2) - 1; // nearest 45-degree heading
            if (previous?.Offset is { } old)
            {
                if (old.X == sx && old.Y == sy) threshold = 0.30;
                else if ((ax >= ay && old.X == sx && old.Y == 0) ||
                    (ay > ax && old.Y == sy && old.X == 0)) threshold = 0.50;
            }
            if (ratio < threshold) { if (ax >= ay) sy = 0; else sx = 0; }
        }
        // Caller is outside the stop radius (>=0.5), so at least one component must remain.
        return FollowStep.Between(start, new(start.X + sx, start.Y + sy));
    }

    public static bool ClearSegment(NavigationFrame grid, double x, double y, double endX, double endY)
    {
        if (!grid.DynamicHazardsKnown || !double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(endX) || !double.IsFinite(endY)) return false;
        var current = new GridPoint((int)Math.Floor(x), (int)Math.Floor(y));
        var end = new GridPoint((int)Math.Floor(endX), (int)Math.Floor(endY));
        if (!grid.Safe(current)) return false;
        var dx = endX - x; var dy = endY - y;
        var sx = Math.Sign(dx); var sy = Math.Sign(dy);
        var deltaX = sx == 0 ? double.PositiveInfinity : 1 / Math.Abs(dx);
        var deltaY = sy == 0 ? double.PositiveInfinity : 1 / Math.Abs(dy);
        var nextX = sx == 0 ? double.PositiveInfinity : ((sx > 0 ? current.X + 1 - x : x - current.X) / Math.Abs(dx));
        var nextY = sy == 0 ? double.PositiveInfinity : ((sy > 0 ? current.Y + 1 - y : y - current.Y) / Math.Abs(dy));
        for (var i = 0; i < 4096; i++)
        {
            if (current == end) return true;
            var crossX = nextX <= nextY + 1e-10;
            var crossY = nextY <= nextX + 1e-10;
            var next = new GridPoint(current.X + (crossX ? sx : 0), current.Y + (crossY ? sy : 0));
            if (!grid.SafeStep(current, FollowStep.Between(current, next))) return false;
            current = next;
            if (crossX) nextX += deltaX;
            if (crossY) nextY += deltaY;
        }
        return false;
    }
}
