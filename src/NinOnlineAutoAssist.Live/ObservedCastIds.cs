namespace NinOnlineAutoAssist.Live;

/// <summary>Read-only, in-memory IDs from two fresh samples of the same local cast. Never maps names to IDs.</summary>
public sealed class ObservedCastIds
{
    private string? _player;
    private int _map;
    private long _lastCapture = -1;
    private (int Spell, long Started, long Total)? _candidate;
    private readonly List<int> _recent = [];
    public IReadOnlyList<int> Recent => _recent.ToArray();

    public void Reset()
    {
        _player = null; _map = 0; _lastCapture = -1; _candidate = null; _recent.Clear();
    }

    public void Observe(LiveFrame? frame, long now)
    {
        if (frame is null || !AutoCastTurnEngine.Fresh(frame, now) ||
            frame.Cast is not { Active: true, Valid: true } cast ||
            (cast.ObservedAtMs is { } observed && (observed < frame.CapturedAtMs ||
                observed > frame.CapturedAtMs + frame.ReadDurationMs || observed > now)) ||
            string.IsNullOrWhiteSpace(frame.PlayerName) || string.IsNullOrWhiteSpace(frame.Snapshot.Player.Id))
        { _candidate = null; return; }
        if (_player != frame.Snapshot.Player.Id || _map != frame.Map || frame.CapturedAtMs < _lastCapture)
        {
            Reset(); _player = frame.Snapshot.Player.Id; _map = frame.Map;
        }
        if (frame.CapturedAtMs <= _lastCapture) return; // UI refresh is not a second memory sample.
        if (_lastCapture >= 0 && frame.CapturedAtMs - _lastCapture > 200) _candidate = null;
        _lastCapture = frame.CapturedAtMs;
        var key = (cast.SpellId, cast.StartedAtGameMs, cast.TotalMs);
        if (_candidate == key)
        {
            _recent.Remove(cast.SpellId);
            _recent.Insert(0, cast.SpellId);
            if (_recent.Count > 20) _recent.RemoveAt(_recent.Count - 1);
        }
        _candidate = key;
    }
}
