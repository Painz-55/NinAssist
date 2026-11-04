namespace NinOnlineAutoAssist.Core.Events;

/// <summary>
/// Provenance of an event. The UI must distinguish these; never present a
/// simulated or requested action as if it were confirmed in the real game.
/// </summary>
public enum EventOrigin
{
    /// <summary>Produced by the local simulator.</summary>
    Simulated = 0,
    /// <summary>Observed from the environment (e.g., log line, memory read) — not yet confirmed.</summary>
    Observed = 1,
    /// <summary>Confirmed by an authoritative signal (e.g., buff icon present, teleport event).</summary>
    Confirmed = 2,
    /// <summary>An action/turn was merely requested (input sent or queued).</summary>
    Requested = 3,
    /// <summary>The requested action was dispatched/executed by the input layer.</summary>
    Executed = 4
}

public enum CombatEventKind
{
    /// <summary>An attack animation/cast was detected on some entity. Not proof of an incoming hit.</summary>
    ThreatDetected = 0,
    /// <summary>The attack actually landed / connected with the player.</summary>
    AttackLanded = 1,
    /// <summary>Substitution buff activation was requested (input sent).</summary>
    BuffActivationRequested = 2,
    /// <summary>Substitution buff confirmed active (authoritative signal).</summary>
    BuffConfirmed = 3,
    /// <summary>Substitution buff expired without triggering.</summary>
    BuffExpired = 4,
    /// <summary>Raw signal that substitution triggered (explicit event from provider).</summary>
    SubstitutionTriggered = 5,
    /// <summary>A position jump was observed; may or may not be a substitution.</summary>
    TeleportObserved = 6,
    /// <summary>Combat state changed (enter/exit combat).</summary>
    CombatStateChanged = 7,
    /// <summary>Repositioning after teleport confirmed (position coherent with teleport).</summary>
    RepositionConfirmed = 8,
    /// <summary>Character confirmed facing the attacker.</summary>
    AlignmentConfirmed = 9,
    /// <summary>An offensive action was requested.</summary>
    ActionRequested = 10,
    /// <summary>An offensive action was dispatched by the input layer.</summary>
    ActionExecuted = 11,
    /// <summary>An offensive action was confirmed as connected (damage/hit event observed).</summary>
    ActionConfirmed = 12,
    /// <summary>The user manually interrupted the automation.</summary>
    ManualInterrupt = 13,
    /// <summary>Proximity/range evidence for the counter-attack window.</summary>
    AttackWindowOpened = 14,
    /// <summary>Internal: sequence aborted. Reason carries the cause.</summary>
    SequenceAborted = 15
}

/// <summary>
/// Flat event record. Optional fields carry the payload relevant to each kind;
/// consumers must null-check. Timestamps come from IMonotonicClock (ms).
/// </summary>
public sealed record CombatEvent
{
    public required CombatEventKind Kind { get; init; }
    public required long TimestampMs { get; init; }
    public EventOrigin Origin { get; init; } = EventOrigin.Observed;

    // Attack / threat payload
    public string? AttackerId { get; init; }
    public string? TargetId { get; init; }
    public string? AttackType { get; init; }
    public double? Distance { get; init; }
    public string? AnimationState { get; init; }
    /// <summary>0..1 confidence reported by the detector.</summary>
    public double? Confidence { get; init; }

    // Position payload
    public double? PositionX { get; init; }
    public double? PositionY { get; init; }

    // Free-form diagnostic note (never used for logic decisions).
    public string? Note { get; init; }

    public static CombatEvent Create(CombatEventKind kind, long nowMs, EventOrigin origin,
        string? attackerId = null, string? note = null) =>
        new() { Kind = kind, TimestampMs = nowMs, Origin = origin, AttackerId = attackerId, Note = note };
}
