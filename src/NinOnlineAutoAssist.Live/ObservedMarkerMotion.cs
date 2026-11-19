namespace NinOnlineAutoAssist.Live;

/// <summary>Visual interpolation strictly between two observed screen positions; never extrapolates.</summary>
public sealed class ObservedMarkerMotion
{
    private sealed record Track(string Name, double StartX, double StartY, double EndX, double EndY,
        long StartedAtMs, int DurationMs);
    private readonly Dictionary<int, Track> _tracks = new();
    public void Reset() => _tracks.Clear();

    public void Set(int slot, string name, double x, double y, long nowMs, bool observed)
    {
        if (slot <= 0 || string.IsNullOrWhiteSpace(name) || !double.IsFinite(x) || !double.IsFinite(y)) return;
        if (!_tracks.TryGetValue(slot, out var old) || old.Name != name)
        { _tracks[slot] = new(name, x, y, x, y, nowMs, 0); return; }
        if (old.EndX == x && old.EndY == y) return;
        // A last-known marker may move on screen because the camera changed, but its world
        // location is not predicted. Keep the interpolation short and bounded.
        var current = Position(slot, nowMs)!.Value;
        _tracks[slot] = new(name, current.X, current.Y, x, y, nowMs, observed ? 130 : 90);
    }

    public (double X, double Y)? Position(int slot, long nowMs)
    {
        if (!_tracks.TryGetValue(slot, out var track)) return null;
        if (track.DurationMs == 0) return (track.EndX, track.EndY);
        var progress = Math.Clamp((double)(nowMs - track.StartedAtMs) / track.DurationMs, 0, 1);
        var eased = progress * progress * (3 - 2 * progress);
        return (track.StartX + (track.EndX - track.StartX) * eased,
            track.StartY + (track.EndY - track.StartY) * eased);
    }

    public void KeepOnly(IReadOnlyCollection<int> slots)
    {
        foreach (var slot in _tracks.Keys.ToArray())
            if (!slots.Contains(slot)) _tracks.Remove(slot);
    }

    public bool IsAnimating(long nowMs) => _tracks.Values.Any(t => nowMs >= t.StartedAtMs &&
        nowMs - t.StartedAtMs < t.DurationMs);
}
