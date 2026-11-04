namespace NinOnlineAutoAssist.Live;

public sealed record MapPlayer(int Slot, string Name, int Map, double? X, double? Y, bool IsLocal, bool IsSelected)
{
    public PlayerVillage Village { get; init; } = PlayerVillage.Unknown;
}

/// <summary>Client-side map records, not a server-wide or visibility-authoritative census.</summary>
public sealed record MapPlayersFrame(int Map, string LocalId, long CapturedAtMs, IReadOnlyList<MapPlayer> Players, int UnreadableSlots)
{
    public PlayerVillage LocalVillage
    {
        get
        {
            var local = Players.Where(p => p.IsLocal).Take(2).ToArray();
            return local.Length == 1 && PlayerVillages.IsKnown(local[0].Village) ? local[0].Village : PlayerVillage.Unknown;
        }
    }

    /// <summary>Presentation only: never removes records from the source used by observation/automation.</summary>
    public IReadOnlyList<MapPlayer> ForDisplay(bool hideOwnVillage)
    {
        if (!hideOwnVillage) return Players;
        var own = LocalVillage;
        if (!PlayerVillages.IsKnown(own)) return Array.Empty<MapPlayer>();
        return Array.AsReadOnly(Players.Where(p => !p.IsLocal && PlayerVillages.IsKnown(p.Village) && p.Village != own).ToArray());
    }

    public bool IsCurrent(LiveFrame? live, long nowMs) => live is not null && live.Map == Map &&
        live.Snapshot.Player.Id == LocalId && nowMs >= CapturedAtMs && nowMs - CapturedAtMs <= 1500 &&
        nowMs >= live.CapturedAtMs && nowMs - live.CapturedAtMs <= 500;

    public static IReadOnlyList<MapPlayer> Filter(IEnumerable<MapPlayer> records, int map) => Array.AsReadOnly(records
        .Where(p => p.Map == map && p.Slot > 0 && !string.IsNullOrWhiteSpace(p.Name))
        .OrderByDescending(p => p.IsLocal).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Slot).ToArray());
}
