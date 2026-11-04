using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Geometry;

namespace NinOnlineAutoAssist.Live;

/// <summary>Observed local cast-bar timing. Null observation means unknown, never idle/completed.</summary>
public sealed record CastObservation(bool Active, int SpellId, long StartedAtGameMs, long TotalMs, long RemainingMs)
{
    public long? ObservedAtMs { get; init; }
    public bool Valid => !Active || (SpellId > 0 && StartedAtGameMs > 0 && TotalMs is >= 100 and <= 60_000 &&
        RemainingMs >= 0 && RemainingMs <= TotalMs);

    public static CastObservation? Decode(int uiSpell, int uiStart, int actorSpell, int actorEnd, long gameNow)
    {
        if (gameNow <= 0) return null;
        if (uiSpell == 0 && uiStart == 0 && actorSpell == 0 && actorEnd == 0) return new(false, 0, 0, 0, 0);
        if (uiSpell <= 0 || actorSpell != uiSpell || uiStart <= 0 || actorEnd <= uiStart || gameNow < uiStart) return null;
        var cast = new CastObservation(true, uiSpell, uiStart, (long)actorEnd - uiStart, Math.Max(0, actorEnd - gameNow));
        return cast.Valid ? cast : null;
    }
}

public sealed record CastTurnRequest(int Map, string PlayerId, string TargetId, int SpellId,
    long StartedAtGameMs, long TotalMs, Direction Direction);

/// <summary>Maintains/rechecks facing in the final cast window; never starts a turn on a falling casting edge.</summary>
public sealed class AutoCastTurnEngine
{
    private readonly IMonotonicClock _clock;
    private readonly AutoCastTurnSettings _settings;
    private readonly Func<Direction, TurnResult> _turn;
    private static readonly DirectionCalculatorOptions Geometry = new() { PositionTolerance = 0.1, HysteresisRatio = 1.25 };
    private bool _sawIdle, _consumed, _sawEarlierProgress;
    private long _lastCapture = -1, _previousRemaining;
    private int _samples;
    private string? _contextPlayer;
    private int _contextMap;
    private long _lastStarted;
    private long? _lastTurnAt;
    private long? _lastReleaseAt;
    private CastTurnRequest? _tracking;
    public CastTurnRequest? Dispatching { get; private set; }
    public bool InFinalWindow { get; private set; }
    public string Status { get; private set; } = "Auto Cast Turn OFF";

    public AutoCastTurnEngine(IMonotonicClock clock, AutoCastTurnSettings settings, Func<Direction, TurnResult> turn)
    { settings.Validate(); _clock = clock; _settings = settings; _turn = turn; }

    public void Reset()
    {
        _sawIdle = _consumed = _sawEarlierProgress = InFinalWindow = false;
        _lastCapture = -1; _samples = 0; _tracking = Dispatching = null;
        _lastTurnAt = _lastReleaseAt = null;
        Status = "Auto Cast Turn: aguardando novo cast completo";
    }

    public static bool Fresh(LiveFrame? frame, long now) => frame is not null && now >= frame.CapturedAtMs &&
        now - frame.CapturedAtMs <= 200 && frame.ReadDurationMs is >= 0 and <= 200;

    private long Window(CastObservation cast) => Math.Min(_settings.LeadTimeMs, cast.TotalMs / 4);
    private static bool TimingValid(LiveFrame frame, CastObservation cast, long now) =>
        cast.ObservedAtMs is not { } observed || (observed >= frame.CapturedAtMs &&
            observed <= frame.CapturedAtMs + frame.ReadDurationMs && observed <= now);
    private static long Remaining(LiveFrame frame, CastObservation cast, long now) =>
        cast.RemainingMs - (now - (cast.ObservedAtMs ?? frame.CapturedAtMs));
    private static Direction Desired(LiveFrame frame)
    {
        if (frame.Snapshot.Target is not { IsAlive: true, IsTargeted: true } target) return Direction.None;
        var player = frame.Snapshot.Player;
        var dx = target.PositionX - player.PositionX; var dy = target.PositionY - player.PositionY;
        return double.IsFinite(dx) && double.IsFinite(dy) && player.Facing != Direction.None
            ? DirectionCalculator.Calculate(dx, dy, player.Facing, Geometry) : Direction.None;
    }

    public bool CanDispatch(LiveFrame? frame, long now)
        => CheckDispatch(frame, now, maintaining: false);

    public bool CanMaintain(LiveFrame? frame, long now)
        => CheckDispatch(frame, now, maintaining: true);

    private bool CheckDispatch(LiveFrame? frame, long now, bool maintaining)
    {
        if (!Fresh(frame, now) || Dispatching is not { } request || frame!.Cast is not { Active: true, Valid: true } cast ||
            frame.Map != request.Map || frame.Snapshot.Player.Id != request.PlayerId || !frame.Snapshot.Player.IsAlive ||
            frame.Snapshot.Target?.Id != request.TargetId || cast.SpellId != request.SpellId ||
            cast.StartedAtGameMs != request.StartedAtGameMs || cast.TotalMs != request.TotalMs) return false;
        if (!TimingValid(frame, cast, now)) return false;
        var remaining = Remaining(frame, cast, now);
        // Only an ALREADY-OWNED key may cross the estimated deadline, for at most 50ms and only while
        // the same cast is still explicitly active. Idle/cancel/context loss always releases immediately.
        if ((maintaining ? remaining < -50 : remaining <= 0) || remaining > Window(cast) || Desired(frame) != request.Direction)
            return false;
        // A held face key is not assumed to defeat movement in every game input poll. If a fresh
        // post-command sample still faces away after 40ms, end this hold so the engine can reapply it.
        return !maintaining || _lastTurnAt is not { } issued || frame.CapturedAtMs <= issued + 40 ||
            frame.Snapshot.Player.Facing == request.Direction;
    }

    public void Update(LiveFrame? frame, bool allowed, bool higherPriorityBusy)
    {
        var now = _clock.NowMs;
        InFinalWindow = false;
        if (!allowed || !Fresh(frame, now) || frame!.Cast is not { Valid: true } cast || !frame.Snapshot.Player.IsAlive || !TimingValid(frame, cast, now))
        { Reset(); Status = !allowed ? "Auto Cast Turn bloqueado/OFF" : "Auto Cast Turn: cast desconhecido/dados antigos"; return; }
        if (_contextPlayer != frame.Snapshot.Player.Id || _contextMap != frame.Map)
        {
            Reset(); _contextPlayer = frame.Snapshot.Player.Id; _contextMap = frame.Map; _lastStarted = 0;
        }
        if (_lastCapture >= 0 && (frame.CapturedAtMs < _lastCapture || frame.CapturedAtMs - _lastCapture > 200))
        { Reset(); Status = "Auto Cast Turn: lacuna de leitura; aguardando novo cast"; }
        var freshSample = frame.CapturedAtMs > _lastCapture;
        _lastCapture = frame.CapturedAtMs;
        if (!cast.Active)
        {
            _sawIdle = true; _tracking = null; _consumed = _sawEarlierProgress = false; _samples = 0;
            _lastTurnAt = _lastReleaseAt = null;
            Status = "Auto Cast Turn: aguardando conjuração"; return;
        }
        if (!_sawIdle) { Status = "Auto Cast Turn: aguardando fim do cast atual para armar o próximo"; return; }
        var target = frame.Snapshot.Target;
        if (_tracking is null || _tracking.SpellId != cast.SpellId || _tracking.StartedAtGameMs != cast.StartedAtGameMs)
        {
            if (cast.StartedAtGameMs <= _lastStarted)
            { Status = "Auto Cast Turn: marcador de cast repetido/antigo; descartado"; return; }
            _lastStarted = cast.StartedAtGameMs;
            _tracking = new(frame.Map, frame.Snapshot.Player.Id, target?.Id ?? "", cast.SpellId, cast.StartedAtGameMs, cast.TotalMs, Direction.None);
            _consumed = false; _sawEarlierProgress = cast.RemainingMs > Window(cast); _samples = 0;
            _previousRemaining = cast.RemainingMs;
            _lastTurnAt = _lastReleaseAt = null;
        }
        if (freshSample) _samples++;
        if (frame.Map != _tracking.Map || frame.Snapshot.Player.Id != _tracking.PlayerId || target is not { IsAlive: true, IsTargeted: true } ||
            target.Id != _tracking.TargetId || cast.TotalMs != _tracking.TotalMs ||
            cast.RemainingMs > _previousRemaining + 50 || higherPriorityBusy)
        { _consumed = true; Status = "Auto Cast Turn: cast descartado por alvo/contexto/prioridade"; return; }
        if (freshSample) _previousRemaining = cast.RemainingMs;
        var remaining = Remaining(frame, cast, now);
        InFinalWindow = remaining > 0 && remaining <= Window(cast);
        if (_consumed) return;
        Status = $"Auto Cast Turn: cast {cast.SpellId} · {Math.Max(0, remaining)} ms restantes";
        if (remaining <= 0) { _consumed = true; Status = "Auto Cast Turn: janela final perdida; sem giro atrasado"; return; }
        if (!InFinalWindow || !_sawEarlierProgress || _samples < 2) return;
        var desired = Desired(frame);
        if (desired == Direction.None || desired == frame.Snapshot.Player.Facing)
        { Status = "Auto Cast Turn: alinhado; vigiando direção até o fim do cast"; return; }
        // Correct only observed drift, not a busy-loop on the same/pre-command snapshot.
        if (_lastTurnAt is { } last && (now - last < 40 || frame.CapturedAtMs <= last)) return;
        // Let the game's input poll observe key-up before reapplying the SAME face binding.
        if (_lastReleaseAt is { } released && now - released < 20) return;
        _lastTurnAt = now;
        Dispatching = _tracking with { Direction = desired };
        try
        {
            var result = _turn(desired);
            if (!result.Accepted) _consumed = true; // Binding/manual-key/fault refusal is terminal for this cast.
            Status = result.Accepted ? $"Auto Cast Turn: mantendo/corrigindo {desired} até o fim do cast; efeito NÃO confirmado"
                : $"Auto Cast Turn: giro recusado — {result.Reason}";
        }
        finally { Dispatching = null; _lastReleaseAt = _clock.NowMs; }
    }
}
