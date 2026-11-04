namespace NinOnlineAutoAssist.Live;

public sealed record ProjectileSourceDefinition(string Name, byte Type, bool WalkCast, int Picture);

/// <summary>Narrow exception for identified local attack shots, never a blanket owner-based exemption.</summary>
public static class ProjectileHazardPolicy
{
    private static readonly HashSet<string> OwnAttackNames = new(StringComparer.Ordinal)
    {
        "Shuriken", "Fan I Basic", "Fan II DMG", "Fan III DMG Range", "Fan IV Triple", "Fan V Long Pierce",
        "Fan VI Crystal", "Fan VII Life Steal", "Fan VIII Triple Short", "Fan V Tornado",
        "Fan II DMG (Knockbacks)(Hyaku)", "Fan VII Double Hit", "Fan VII Double Hit (2)",
        "Fan Jinbu Double Hit", "Fan Jinbu Double Hit (2)"
    };
    public static bool IgnoreOwnAttack(int localIndex, int owner, int ownerType, int range, int speed, int picture,
        ProjectileSourceDefinition? source)
        => localIndex > 0 && owner == localIndex && ownerType == 1 && range is > 0 and <= 64 && speed is > 0 and <= 1000 &&
            source is { Type: 4, WalkCast: true } && source.Picture == picture && OwnAttackNames.Contains(source.Name);
}
