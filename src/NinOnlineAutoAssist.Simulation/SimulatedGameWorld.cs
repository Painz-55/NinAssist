using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Events;
using NinOnlineAutoAssist.Core.Models;

namespace NinOnlineAutoAssist.Simulation;

/// <summary>
/// Deterministic scripted world used only by automated tests, never by the live app.
/// It NEVER talks to the game process; every event it emits is Origin=Simulated.
/// </summary>
public sealed class SimulatedGameWorld : IGameStateProvider
{
    public const string SubBuffId = "substitution";

    private readonly IMonotonicClock _clock;
    private readonly List<EntityState> _entities = new();
    private readonly List<(long FireAtMs, Func<CombatEvent> Factory)> _scheduled = new();
    private readonly List<CombatEvent> _emittedThisDrain = new();

    // Player
    public double PlayerX { get; set; }
    public double PlayerY { get; set; }
    public Direction PlayerFacing { get; set; } = Direction.Down;
    public bool PlayerIsAlive { get; set; } = true;
    public bool PlayerIsCasting { get; set; }
    public bool PlayerIsActionBlocked { get; set; }
    public int PlayerResources { get; set; } = 100;
    public string PlayerId { get; set; } = "player";

    // World state
    public string? SelectedTargetId { get; private set; }
    public bool InCombat { get; private set; }
    public long? ProviderSubstitutionCooldownMs { get; set; }
    public bool SuppressTeleportEvidence { get; set; }
    public long SubBuffExpiresAtMs { get; private set; } = -1;

    public SimulatedGameWorld(IMonotonicClock clock) => _clock = clock;

    public string Status => "SIMULATION — scripted world, no game process attached";
    public bool IsLiveData => false;

    // ── entities ──────────────────────────────────────────────────────
    private sealed class EntityState
    {
        public required string Id; public double X; public double Y; public bool IsAlive = true;
    }

    public void SpawnTarget(string id, double x, double y)
    {
        _entities.RemoveAll(e => e.Id == id);
        _entities.Add(new EntityState { Id = id, X = x, Y = y });
    }

    public void MoveTarget(string id, double x, double y)
    {
        var e = _entities.FirstOrDefault(e => e.Id == id);
        if (e is not null) { e.X = x; e.Y = y; }
    }

    public void RemoveTarget(string id) => _entities.RemoveAll(e => e.Id == id);
    public void SetTargetAlive(string id, bool alive)
    {
        var e = _entities.FirstOrDefault(e => e.Id == id);
        if (e is not null) e.IsAlive = alive;
    }

    public void SelectTarget(string? id) => SelectedTargetId = id;

    // ── snapshot ──────────────────────────────────────────────────────
    public GameSnapshot? GetSnapshot()
    {
        var player = new PlayerState
        {
            Id = PlayerId,
            PositionX = PlayerX,
            PositionY = PlayerY,
            Facing = PlayerFacing,
            IsAlive = PlayerIsAlive,
            IsCasting = PlayerIsCasting,
            IsActionBlocked = PlayerIsActionBlocked,
            Resources = PlayerResources
        };

        TargetInfo? selected = null;
        if (SelectedTargetId is { } sid)
        {
            var e = _entities.FirstOrDefault(e => e.Id == sid);
            if (e is not null) selected = new TargetInfo { Id = e.Id, PositionX = e.X, PositionY = e.Y, IsAlive = e.IsAlive, IsTargeted = true };
        }

        var known = _entities
            .Select(e => new TargetInfo { Id = e.Id, PositionX = e.X, PositionY = e.Y, IsAlive = e.IsAlive, IsTargeted = e.Id == SelectedTargetId })
            .ToList();

        var buffs = SubBuffExpiresAtMs > _clock.NowMs ? new List<string> { SubBuffId } : new List<string>();

        return new GameSnapshot
        {
            Player = player,
            Target = selected,
            KnownEntities = known,
            InCombat = InCombat,
            ActiveBuffs = buffs,
            SubstitutionCooldownRemainingMs = ProviderSubstitutionCooldownMs
        };
    }

    // ── scripting API ─────────────────────────────────────────────────
    public void Schedule(long delayMs, CombatEventKind kind, string? attackerId = null,
        double? distance = null, string? attackType = null, string? animationState = null,
        double? confidence = null, string? note = null, double? x = null, double? y = null)
    {
        long fireAt = _clock.NowMs + delayMs;
        _scheduled.Add((fireAt, () => new CombatEvent
        {
            Kind = kind,
            TimestampMs = _clock.NowMs,
            Origin = EventOrigin.Simulated,
            AttackerId = attackerId,
            TargetId = PlayerId,
            Distance = distance,
            AttackType = attackType,
            AnimationState = animationState,
            Confidence = confidence,
            Note = note,
            PositionX = x,
            PositionY = y
        }));
    }

    /// <summary>Convenience: a committed attack animation heading towards the player.</summary>
    public void SimulateThreat(string attackerId, double distance = 3.0, double confidence = 0.9,
        string animationState = "attack_release", string attackType = "melee") =>
        DrainAndEmit(new CombatEvent
        {
            Kind = CombatEventKind.ThreatDetected, TimestampMs = _clock.NowMs, Origin = EventOrigin.Simulated,
            AttackerId = attackerId, TargetId = PlayerId, Distance = distance,
            Confidence = confidence, AnimationState = animationState, AttackType = attackType
        });

    /// <summary>
    /// The attack connects with the player. If the substitution buff is active, the sub
    /// consumes it: the player is teleported behind the attacker and the appropriate
    /// events are emitted (or only a bare trigger when SuppressTeleportEvidence is set).
    /// </summary>
    public void SimulateAttackLanded(string attackerId)
    {
        bool subActive = SubBuffExpiresAtMs > _clock.NowMs;

        DrainAndEmit(new CombatEvent
        {
            Kind = CombatEventKind.AttackLanded, TimestampMs = _clock.NowMs, Origin = EventOrigin.Simulated,
            AttackerId = attackerId, TargetId = PlayerId, AttackType = "melee"
        });

        InCombat = true;

        if (!subActive) return; // Hit lands, no substitution.

        // Consume the buff.
        SubBuffExpiresAtMs = -1;

        if (SuppressTeleportEvidence)
        {
            // A trigger-ish noise WITHOUT corroborating evidence (buff disappearance,
            // teleport, combat-state change) — confirmation must fail.
            DrainAndEmit(new CombatEvent
            {
                Kind = CombatEventKind.SubstitutionTriggered, TimestampMs = _clock.NowMs,
                Origin = EventOrigin.Simulated, AttackerId = attackerId, TargetId = PlayerId,
                Note = "trigger noise without corroboration"
            });
            return;
        }

        // Teleport behind the attacker (classic sub behaviour, simulated): lands directly
        // behind them at attacker + 1.2u, outside micro-movement noise.
        var attacker = _entities.FirstOrDefault(e => e.Id == attackerId);
        if (attacker is not null)
        {
            PlayerX = attacker.X;
            PlayerY = attacker.Y + 1.2;
        }

        DrainAndEmit(new CombatEvent { Kind = CombatEventKind.SubstitutionTriggered, TimestampMs = _clock.NowMs, Origin = EventOrigin.Simulated, AttackerId = attackerId, TargetId = PlayerId });
        DrainAndEmit(new CombatEvent { Kind = CombatEventKind.TeleportObserved, TimestampMs = _clock.NowMs, Origin = EventOrigin.Simulated, AttackerId = attackerId, TargetId = PlayerId, PositionX = PlayerX, PositionY = PlayerY });
        DrainAndEmit(new CombatEvent { Kind = CombatEventKind.CombatStateChanged, TimestampMs = _clock.NowMs, Origin = EventOrigin.Simulated, AttackerId = attackerId, TargetId = PlayerId });
        DrainAndEmit(new CombatEvent { Kind = CombatEventKind.BuffExpired, TimestampMs = _clock.NowMs, Origin = EventOrigin.Simulated, Note = "buff consumed by substitution" });
    }

    /// <summary>Activate the substitution buff (as the game would after a successful cast).</summary>
    public void SimulateBuffConfirmed(double durationSec = 7.0)
    {
        SubBuffExpiresAtMs = _clock.NowMs + (long)(durationSec * 1000);
        DrainAndEmit(new CombatEvent { Kind = CombatEventKind.BuffConfirmed, TimestampMs = _clock.NowMs, Origin = EventOrigin.Simulated });
    }

    /// <summary>Expire the buff naturally (no attack received).</summary>
    public void SimulateBuffExpired()
    {
        SubBuffExpiresAtMs = -1;
        DrainAndEmit(new CombatEvent { Kind = CombatEventKind.BuffExpired, TimestampMs = _clock.NowMs, Origin = EventOrigin.Simulated, Note = "natural expiry" });
    }

    public void SimulateManualInterrupt() =>
        DrainAndEmit(new CombatEvent { Kind = CombatEventKind.ManualInterrupt, TimestampMs = _clock.NowMs, Origin = EventOrigin.Simulated });

    private void DrainAndEmit(CombatEvent evt) => _emittedThisDrain.Add(evt);

    /// <summary>
    /// Returns and clears events produced by the simulation since the last drain,
    /// including scheduled events whose time has come and buff expiry.
    /// </summary>
    public IReadOnlyList<CombatEvent> DrainDueEvents()
    {
        var now = _clock.NowMs;

        // Natural buff expiry.
        if (SubBuffExpiresAtMs > 0 && now >= SubBuffExpiresAtMs)
        {
            SubBuffExpiresAtMs = -1;
            _emittedThisDrain.Add(new CombatEvent { Kind = CombatEventKind.BuffExpired, TimestampMs = now, Origin = EventOrigin.Simulated, Note = "natural expiry" });
        }

        var due = _scheduled.Where(s => s.FireAtMs <= now).OrderBy(s => s.FireAtMs).ToList();
        foreach (var (_, factory) in due) _emittedThisDrain.Add(factory());
        _scheduled.RemoveAll(s => s.FireAtMs <= now);

        var result = _emittedThisDrain.ToList();
        _emittedThisDrain.Clear();
        return result;
    }
}
