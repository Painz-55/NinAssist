namespace NinOnlineAutoAssist.Live;

/// <summary>Exact spell/buff names from the client catalog, never partial-name matches.</summary>
public static class SubstitutionBuffs
{
    public const string Normal = "Substitution Technique";
    public const string Water = "Water Substitution Technique";
    // Observed on Skyrend together with normal Sub, with its own 5→0 countdown.
    public const string Tai = "Speed Mirage Technique";
    public static bool IsKnown(string name) => name is Normal or Water or Tai;
    public static string Label(string name) => name switch { Normal => "Normal", Water => "Água", Tai => "Tai", _ => name };

    public static IReadOnlyList<ObservedBuff> Active(LiveFrame frame)
    {
        if (frame.ObservedActiveBuffs.Count > 0)
            return frame.ObservedActiveBuffs.Where(b => IsKnown(b.Name)).ToArray();
        // Compatibility for existing single-buff test fixtures, not an assumed live duration.
        return frame.SubstitutionActive
            ? new[] { new ObservedBuff(Normal, frame.SubstitutionDuration) } : Array.Empty<ObservedBuff>();
    }
}

internal sealed record EarlyRemoval(string Name, long RemainingMs);
internal sealed record RemovalSample(IReadOnlyList<EarlyRemoval> Ready, int PendingSamples,
    bool Watching, bool KnownDuration, bool Expired, bool NewCycle);

/// <summary>Independent conservative countdown/absence evidence for each overlapping Sub.</summary>
internal sealed class SubstitutionRemovalTracker
{
    private sealed class Cycle
    {
        public long? Expiry;
        public bool WasActive, Consumed;
        public int Absent;
    }
    private readonly Dictionary<string, Cycle> _cycles = new(StringComparer.Ordinal);
    public void Reset() => _cycles.Clear();

    public RemovalSample Update(LiveFrame frame, long nowMs, long marginMs, int samplesRequired)
    {
        var active = SubstitutionBuffs.Active(frame).GroupBy(b => b.Name).ToDictionary(g => g.Key, g =>
            new ObservedBuff(g.Key, g.Any(b => b.RemainingSeconds is null) ? null : g.Min(b => b.RemainingSeconds)));
        foreach (var name in active.Keys) _cycles.TryAdd(name, new Cycle());
        var ready = new List<EarlyRemoval>();
        int pending = 0;
        bool known = false, expired = false, newCycle = false;
        foreach (var (name, cycle) in _cycles)
        {
            if (active.TryGetValue(name, out var buff))
            {
                if (!cycle.WasActive && (cycle.Absent == 0 || cycle.Consumed))
                { cycle.Expiry = null; cycle.Consumed = false; newCycle = true; }
                cycle.WasActive = true; cycle.Absent = 0;
                if (buff.RemainingSeconds is >= 0 and <= 60)
                {
                    var expiry = frame.CapturedAtMs + buff.RemainingSeconds.Value * 1000L;
                    cycle.Expiry = cycle.Expiry is { } old ? Math.Min(old, expiry) : expiry;
                    known = true;
                }
                else cycle.Expiry = null;
                continue;
            }
            if (!cycle.WasActive && cycle.Absent == 0) continue;
            cycle.WasActive = false;
            if (cycle.Consumed || cycle.Expiry is null) { cycle.Absent = 0; continue; }
            var remaining = cycle.Expiry.Value - nowMs;
            if (remaining <= marginMs)
            { cycle.Consumed = true; cycle.Absent = 0; expired = true; continue; }
            cycle.Absent++;
            if (cycle.Absent >= samplesRequired)
            {
                cycle.Consumed = true; cycle.Absent = 0;
                ready.Add(new EarlyRemoval(name, remaining));
            }
            else pending = Math.Max(pending, cycle.Absent);
        }
        return new(ready, pending, active.Count > 0, known, expired, newCycle);
    }
}
