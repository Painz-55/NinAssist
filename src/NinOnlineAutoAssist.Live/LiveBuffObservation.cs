namespace NinOnlineAutoAssist.Live;

public enum BuffPresence { Unknown, Inactive, Active }

/// <summary>Read status, independent of focus, target selection, module switches or input permission.</summary>
public sealed record LiveBuffObservation(BuffPresence Presence, int? DurationSeconds, string Reason)
{
    public IReadOnlyList<ObservedBuff> ActiveSubs { get; init; } = Array.Empty<ObservedBuff>();
    public static LiveBuffObservation FromFrame(LiveFrame? frame, long nowMs)
    {
        if (frame is null || nowMs < frame.CapturedAtMs || nowMs - frame.CapturedAtMs > 500 ||
            frame.ReadDurationMs is < 0 or > 500)
            return new(BuffPresence.Unknown, null, "Sem amostra recente — não significa buff inativo");
        var active = SubstitutionBuffs.Active(frame);
        return active.Count > 0
            ? new(BuffPresence.Active, frame.SubstitutionDuration, string.Join(" + ", active.Select(b => b.Name))) { ActiveSubs = active }
            : new(BuffPresence.Inactive, null, "Subs monitoradas ausentes na amostra recente");
    }
}
