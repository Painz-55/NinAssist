namespace NinOnlineAutoAssist.Live;

public sealed record SubTeleportEvidence(long CapturedAtMs, double Distance, IReadOnlyList<string> Sources);

/// <summary>Observed position discontinuity, NOT a server-confirmed teleport/substitution event.</summary>
public static class SubTeleportCorrelation
{
    public const long WindowMs = 120;
    public static SubTeleportEvidence? Observe(LiveFrame before, LiveFrame current, IEnumerable<string>? recentlyRemoved = null)
    {
        var dt = current.CapturedAtMs - before.CapturedAtMs;
        if (dt is <= 0 or > 100 || before.Map != current.Map ||
            before.Snapshot.Player.Id != current.Snapshot.Player.Id || !before.Snapshot.Player.IsAlive || !current.Snapshot.Player.IsAlive)
            return null;
        var sources = SubstitutionBuffs.Active(before).Concat(SubstitutionBuffs.Active(current))
            .Select(b => b.Name).Concat(recentlyRemoved ?? Array.Empty<string>()).Where(SubstitutionBuffs.IsKnown)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (sources.Length == 0) return null;
        var dx = current.Snapshot.Player.PositionX - before.Snapshot.Player.PositionX;
        var dy = current.Snapshot.Player.PositionY - before.Snapshot.Player.PositionY;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        // Running alone reached ~8 tiles/s in control; dash ALSO can exceed this threshold.
        // Therefore this signal must be joined to an independently observed EARLY buff removal.
        if (!double.IsFinite(distance) || distance < 1.25 || distance > 8 || distance * 1000 / dt < 20) return null;
        return new(current.CapturedAtMs, distance, sources);
    }
}
