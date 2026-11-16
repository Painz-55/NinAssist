namespace NinOnlineAutoAssist.Live;

/// <summary>Read-only overlay candidates. Client records are not proof of a visible actor or a real-time server location.</summary>
public static class NearbyPlayerMarkers
{
    public sealed record Marker(int Slot, string Name, double OffsetX, double OffsetY, bool Selected,
        long ObservationAgeMs = 0, long MotionAgeMs = 0, bool CurrentlyReported = true);

    public static IReadOnlyList<Marker> Select(MapPlayersFrame? list, LiveFrame? frame, long nowMs,
        double radiusTiles = 750, int limit = 256)
    {
        if (list is null || frame is null || !list.IsCurrent(frame, nowMs) ||
            nowMs - list.CapturedAtMs > 300 ||
            !double.IsFinite(radiusTiles) || radiusTiles is <= 0 or > 750 || limit is < 1 or > 512)
            return Array.Empty<Marker>();
        var x = frame.Snapshot.Player.PositionX;
        var y = frame.Snapshot.Player.PositionY;
        if (!double.IsFinite(x) || !double.IsFinite(y)) return Array.Empty<Marker>();
        return Array.AsReadOnly(list.Players.Where(p => !p.IsLocal && p.X is { } px && p.Y is { } py &&
                double.IsFinite(px) && double.IsFinite(py) &&
                Math.Pow(px - x, 2) + Math.Pow(py - y, 2) <= radiusTiles * radiusTiles)
            .OrderBy(p => p.IsSelected ? 0 : 1)
            .ThenBy(p => Math.Pow(p.X!.Value - x, 2) + Math.Pow(p.Y!.Value - y, 2))
            .Take(limit)
            .Select(p => new Marker(p.Slot, p.Name, p.X!.Value - x, p.Y!.Value - y, p.IsSelected))
            .ToArray());
    }
}
