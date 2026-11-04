using NinOnlineAutoAssist.Core.Events;
using NinOnlineAutoAssist.Core.Geometry;

namespace NinOnlineAutoAssist.Live;

/// <summary>Only buff and positional observations. Never fabricates a threat, hit or attacker identity.</summary>
public sealed class LiveEventDetector
{
    public const long CorrelationWindowMs = 400;
    private LiveFrame? _previous;
    private (LiveFrame ActiveFrame, long RemovedAt)? _pendingRemoval;
    private long? _recentJumpAt;
    public void Reset() { _previous = null; _pendingRemoval = null; _recentJumpAt = null; }

    public IReadOnlyList<CombatEvent> Observe(LiveFrame frame, bool manualMovement, double teleportMinDistance = 1.5)
    {
        var events = new List<CombatEvent>();
        var before = _previous;
        _previous = frame;
        if (before is not null && (before.Map != frame.Map ||
            before.Snapshot.Player.Id != frame.Snapshot.Player.Id || frame.CapturedAtMs <= before.CapturedAtMs ||
            frame.CapturedAtMs - before.CapturedAtMs > 500))
        {
            Reset();
            _previous = frame;
            events.Add(CombatEvent.Create(CombatEventKind.ManualInterrupt, frame.CapturedAtMs, EventOrigin.Observed,
                note: "Map, identity or observation continuity changed"));
            return events;
        }
        // A jump with no correlated buff removal must not be credited much later at natural expiry.
        if (_recentJumpAt is { } jumpAt && frame.CapturedAtMs - jumpAt > CorrelationWindowMs)
        {
            _recentJumpAt = null;
            _pendingRemoval = null;
            events.Add(CombatEvent.Create(CombatEventKind.ManualInterrupt, frame.CapturedAtMs, EventOrigin.Observed,
                note: "Position jump not corroborated by buff disappearance within correlation window"));
            return events;
        }
        if (_pendingRemoval is { } pending)
        {
            _pendingRemoval = null;
            if (frame.SubstitutionActive)
            {
                // One transient absent sample followed by the same buff is not a new activation/trigger.
                return events;
            }
            var elapsed = frame.CapturedAtMs - pending.RemovedAt;
            if (!manualMovement && elapsed <= CorrelationWindowMs && IsJump(pending.ActiveFrame, frame, teleportMinDistance))
            {
                events.Add(Teleport(frame, "Position update arrived after buff removal; correlated within bounded window"));
                events.Add(Expired(frame));
                return events;
            }
            if (manualMovement || elapsed >= CorrelationWindowMs)
                events.Add(Expired(frame));
            else _pendingRemoval = pending;
            return events;
        }
        if (frame.SubstitutionActive && before?.SubstitutionActive != true)
            events.Add(CombatEvent.Create(CombatEventKind.BuffConfirmed, frame.CapturedAtMs, EventOrigin.Observed,
                note: "Substitution Technique present in active buff array"));
        if (before?.SubstitutionActive == true)
        {
            var jumped = !manualMovement && IsJump(before, frame, teleportMinDistance);
            if (jumped)
            {
                events.Add(Teleport(frame, "Position jump while previously buffed; NOT explicit trigger/attacker identification"));
                _recentJumpAt = frame.CapturedAtMs;
            }
            if (!frame.SubstitutionActive)
            {
                if (jumped || _recentJumpAt is not null || manualMovement || before.SubstitutionDuration is not > 0)
                {
                    events.Add(Expired(frame));
                    _recentJumpAt = null;
                }
                else
                    // Do not close the engine before a position packet arriving one sample later.
                    _pendingRemoval = (before, frame.CapturedAtMs);
            }
        }
        return events;
    }

    private static bool IsJump(LiveFrame before, LiveFrame after, double minimum)
    {
        if (before.SubstitutionDuration is not > 0) return false;
        var a = before.Snapshot.Player;
        var b = after.Snapshot.Player;
        var distance = DirectionCalculator.Distance(b.PositionX - a.PositionX, b.PositionY - a.PositionY);
        return distance >= minimum && distance <= 30;
    }

    private static CombatEvent Teleport(LiveFrame frame, string note) => new()
    {
        Kind = CombatEventKind.TeleportObserved, TimestampMs = frame.CapturedAtMs, Origin = EventOrigin.Observed,
        PositionX = frame.Snapshot.Player.PositionX, PositionY = frame.Snapshot.Player.PositionY, Note = note
    };

    private static CombatEvent Expired(LiveFrame frame) => CombatEvent.Create(CombatEventKind.BuffExpired,
        frame.CapturedAtMs, EventOrigin.Observed, note: "Buff disappeared; alone this is not a substitution trigger");
}
