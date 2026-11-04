namespace NinOnlineAutoAssist.Core.Models;

/// <summary>Snapshot of the local player, as reported by an IGameStateProvider.</summary>
public sealed record PlayerState
{
    /// <summary>Provider-assigned identifier of the local player; "local" when the provider cannot supply one.</summary>
    public string Id { get; init; } = "local";
    public double PositionX { get; init; }
    public double PositionY { get; init; }
    public Direction Facing { get; init; } = Direction.None;
    public bool IsAlive { get; init; } = true;
    /// <summary>True while the player is mid-cast / in a committed animation lock.</summary>
    public bool IsCasting { get; init; }
    /// <summary>True when an action/animation block prevents starting new actions.</summary>
    public bool IsActionBlocked { get; init; }
    /// <summary>Available resources (e.g., chakra/MP). Null when unknown.</summary>
    public int? Resources { get; init; }
}

/// <summary>Snapshot of a target (selected enemy or attacker).</summary>
public sealed record TargetInfo
{
    public required string Id { get; init; }
    public double PositionX { get; init; }
    public double PositionY { get; init; }
    public bool IsAlive { get; init; } = true;
    /// <summary>True when this entity is the currently selected target.</summary>
    public bool IsTargeted { get; init; }
}

/// <summary>
/// Aggregate snapshot passed to engines on each tick.
/// </summary>
public sealed record GameSnapshot
{
    public required PlayerState Player { get; init; }

    /// <summary>
    /// The attacker/target relevant for this tick, if any.
    /// Consumers must tolerate null (no valid target).
    /// </summary>
    public TargetInfo? Target { get; init; }

    /// <summary>All currently visible/known entities (optional, may be empty).</summary>
    public IReadOnlyList<TargetInfo> KnownEntities { get; init; } = Array.Empty<TargetInfo>();

    /// <summary>True when the player is in combat, as reported by the provider, or null when unknown.</summary>
    public bool? InCombat { get; init; }

    /// <summary>Ids of active buffs as reported by the provider, or empty when unknown.</summary>
    public IReadOnlyList<string> ActiveBuffs { get; init; } = Array.Empty<string>();

    /// <summary>Remaining cooldown of the substitution technique in ms, or null when unknown.</summary>
    public long? SubstitutionCooldownRemainingMs { get; init; }
}
