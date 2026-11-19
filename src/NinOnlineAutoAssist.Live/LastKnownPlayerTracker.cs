namespace NinOnlineAutoAssist.Live;

/// <summary>Presentation-only last-known locations. Never classifies a client record as a live invisible player.</summary>
public sealed class LastKnownPlayerTracker
{
    private sealed record Entry(string Name, double X, double Y, PlayerVillage Village,
        bool Selected, long SeenAtMs, long MovedAtMs);
    private readonly Dictionary<int, Entry> _entries = new();
    private int _map;
    private string? _localId;
    private long _lastCapture = -1;
    private const long RetentionMs = 7_000;

    public void Reset() { _entries.Clear(); _map = 0; _localId = null; _lastCapture = -1; }

    public IReadOnlyList<NearbyPlayerMarkers.Marker> Update(MapPlayersFrame? list, LiveFrame? frame, long nowMs,
        bool hideOwnVillage = false, IReadOnlyCollection<PlayerVillage>? hiddenVillages = null,
        int maxCensusAgeMs = 300)
    {
        if (frame is null || (_localId is not null &&
            (_localId != frame.Snapshot.Player.Id || _map != frame.Map)))
        { Reset(); return []; }
        // A failed census is not a disappearance event. Preserve the cache but never project
        // with an old view; only a subsequent fresh list may show last-known positions.
        if (list is null || !list.IsCurrent(frame, nowMs) ||
            nowMs - list.CapturedAtMs > maxCensusAgeMs || list.RenderView is null) return [];
        if (_map != list.Map || _localId != list.LocalId || _lastCapture > list.CapturedAtMs)
        { Reset(); _map = list.Map; _localId = list.LocalId; }
        if (_lastCapture != list.CapturedAtMs)
        {
            _lastCapture = list.CapturedAtMs;
            foreach (var player in list.Players.Where(p => !p.IsLocal))
            {
                if (player.X is not { } x || player.Y is not { } y ||
                    !double.IsFinite(x) || !double.IsFinite(y)) continue;
                var old = _entries.GetValueOrDefault(player.Slot);
                var same = old?.Name == player.Name;
                var moved = same && Math.Abs(old!.X - x) < 0.05 && Math.Abs(old.Y - y) < 0.05
                    ? old.MovedAtMs : list.CapturedAtMs;
                _entries[player.Slot] = new(player.Name, x, y, player.Village,
                    player.IsSelected, list.CapturedAtMs, moved);
            }
        }
        foreach (var (slot, entry) in _entries.ToArray())
            if (nowMs < entry.SeenAtMs || nowMs - entry.SeenAtMs > RetentionMs)
                _entries.Remove(slot);
        var xLocal = frame.Snapshot.Player.PositionX;
        var yLocal = frame.Snapshot.Player.PositionY;
        if (!double.IsFinite(xLocal) || !double.IsFinite(yLocal)) { Reset(); return []; }
        var hidden = hiddenVillages ?? Array.Empty<PlayerVillage>();
        return Array.AsReadOnly(_entries.Where(e => MapPlayersFrame.VillageVisible(e.Value.Village,
                isLocal: false, list.LocalVillage, hideOwnVillage, hidden))
            .Select(e => new NearbyPlayerMarkers.Marker(e.Key, e.Value.Name,
                e.Value.X - xLocal, e.Value.Y - yLocal, e.Value.Selected,
                nowMs - e.Value.SeenAtMs, nowMs - e.Value.MovedAtMs,
                e.Value.SeenAtMs == list.CapturedAtMs))
            .OrderBy(m => m.CurrentlyReported ? 0 : 1)
            .ThenBy(m => m.ObservationAgeMs).Take(256).ToArray());
    }
}
