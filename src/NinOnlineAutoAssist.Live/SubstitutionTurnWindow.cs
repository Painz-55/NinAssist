namespace NinOnlineAutoAssist.Live;

/// <summary>Observed buff lifetime plus exactly one second after its first observed absence.</summary>
public sealed class SubstitutionTurnWindow
{
    private LiveFrame? _previous;
    private long? _removedAt;

    public void Reset() { _previous = null; _removedAt = null; }

    public void Update(LiveFrame? frame, long nowMs, bool allowed)
    {
        if (!allowed || !Fresh(frame, nowMs)) { Reset(); return; }
        var before = _previous;
        if (before is not null && (before.Map != frame!.Map || before.Snapshot.Player.Id != frame.Snapshot.Player.Id ||
            frame.CapturedAtMs < before.CapturedAtMs || frame.CapturedAtMs - before.CapturedAtMs > 500))
        { Reset(); return; }
        if (before?.CapturedAtMs == frame!.CapturedAtMs) return;
        if (frame.SubstitutionActive) _removedAt = null;
        else if (before?.SubstitutionActive == true) _removedAt = frame.CapturedAtMs;
        _previous = frame;
    }

    // Rechecked against the latest published frame at EVERY native dispatch and during a held turn key.
    public bool IsOpen(LiveFrame? frame, long nowMs) => Fresh(frame, nowMs) && _previous is { } before &&
        frame!.Map == before.Map && frame.Snapshot.Player.Id == before.Snapshot.Player.Id &&
        frame.CapturedAtMs >= before.CapturedAtMs && frame.CapturedAtMs - before.CapturedAtMs <= 500 &&
        ((frame.SubstitutionActive && before.SubstitutionActive) ||
            (!frame.SubstitutionActive && _removedAt is { } removed && nowMs >= removed && nowMs - removed < 1000));

    private static bool Fresh(LiveFrame? frame, long nowMs) => frame is not null && frame.Snapshot.Player.IsAlive &&
        nowMs >= frame.CapturedAtMs && nowMs - frame.CapturedAtMs <= 500 && frame.ReadDurationMs is >= 0 and <= 500;
}
