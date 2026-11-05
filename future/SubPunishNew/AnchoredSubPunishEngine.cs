using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Events;
using NinOnlineAutoAssist.Core.Logging;

namespace NinOnlineAutoAssist.Live;

/// <summary>Independent experimental trigger: early removal + local position discontinuity in a short window.
/// Archived for future research; NOT compiled or reachable from the application.
/// Does not modify or silently fall back to the legacy trigger. Correlation is not proof of server activation.</summary>
public sealed class AnchoredSubPunishEngine : ILivePunishEngine
{
    private static readonly string[] Actions = { "basic_attack", "followup_hotbar_Q", "followup_hotbar_S" };
    private readonly IActionExecutor _executor;
    private readonly IMonotonicClock _clock;
    private readonly IAutoAssistLogger _log;
    private readonly EarlyPunishOptions _options;
    private readonly SubstitutionRemovalTracker _tracker = new();
    private readonly HashSet<string> _sources = new(StringComparer.Ordinal);
    private LiveFrame? _previous;
    private SubTeleportEvidence? _jump;
    private IReadOnlyList<EarlyRemoval> _pending = Array.Empty<EarlyRemoval>();
    private long _pendingAt, _readyAt, _lastKeyAt = -1, _finishedAt = -1;
    private int _nextKey;
    public EarlyPunishState State { get; private set; }
    public string Status { get; private set; } = "NOVO EXPERIMENTAL: aguardando Sub + deslocamento correlacionado";
    public long? NextKeyDueAtMs => State != EarlyPunishState.Executing ? null : _nextKey switch
    {
        0 => _readyAt, 1 => _lastKeyAt + (_options.FirstToSecondMs ?? _options.KeyIntervalMs),
        _ => _lastKeyAt + (_options.SecondToThirdMs ?? _options.KeyIntervalMs)
    };
    public bool HasTimeForTurn => NextKeyDueAtMs is not { } due || due - _clock.NowMs > 60;
    public bool BlocksAutoTurning => State is EarlyPunishState.RemovalPending or EarlyPunishState.Executing ||
        (_finishedAt >= 0 && _clock.NowMs - _finishedAt < _options.KeyIntervalMs);
    public bool TriggerBuffsRemainAbsent(LiveFrame frame) => _sources.Count > 0 &&
        !SubstitutionBuffs.Active(frame).Any(b => _sources.Contains(b.Name));
    public bool HasUnprocessedRemoval(LiveFrame? frame) => frame is not null && _previous is { } before &&
        frame.CapturedAtMs > before.CapturedAtMs && SubstitutionBuffs.Active(before)
            .Any(old => !SubstitutionBuffs.Active(frame).Any(b => b.Name == old.Name));

    public AnchoredSubPunishEngine(IActionExecutor executor, IMonotonicClock clock, EarlyPunishOptions? options = null,
        IAutoAssistLogger? logger = null)
    {
        _executor = executor; _clock = clock; _log = logger ?? NullLogger.Instance;
        var o = options ?? new EarlyPunishOptions();
        if (o.TriggerDelayMs is < 0 or > 10000 || o.EarlyExpiryMarginMs is < 0 or > 60000 ||
            o.KeyIntervalMs is < 40 or > 2000 || o.SequenceTimeoutMs < o.KeyIntervalMs * 3 ||
            o.ComboKeys is null || o.ComboKeys.Length != 3 || o.ComboKeys.Any(string.IsNullOrWhiteSpace) ||
            o.FirstToSecondMs is < 0 or > 2000 || o.SecondToThirdMs is < 0 or > 2000 ||
            (o.FirstToSecondMs ?? o.KeyIntervalMs) + (o.SecondToThirdMs ?? o.KeyIntervalMs) >= o.SequenceTimeoutMs)
            throw new ArgumentException("Invalid anchored combo options");
        _options = o with { ComboKeys = o.ComboKeys.ToArray() };
    }
    public void Reset()
    {
        _previous = null; _jump = null; _pending = Array.Empty<EarlyRemoval>(); _sources.Clear(); _tracker.Reset();
        _nextKey = 0; _pendingAt = _readyAt = 0; _lastKeyAt = _finishedAt = -1;
        State = EarlyPunishState.Idle;
        Status = "NOVO EXPERIMENTAL: aguardando Sub + deslocamento correlacionado";
    }
    private void Cancel(string reason)
    {
        var busy = State is EarlyPunishState.RemovalPending or EarlyPunishState.Executing;
        Reset(); if (busy) _finishedAt = _clock.NowMs;
        State = EarlyPunishState.Cancelled; Status = $"NOVO cancelado: {reason}";
        if (busy) _log.Log(LogLevel.Warning, Status, EventOrigin.Observed);
    }
    public void Update(LiveFrame? frame, bool enabled, bool authorized, bool safetyAllowed, string? blockReason = null)
    {
        if (!enabled || !authorized || !safetyAllowed)
        { Cancel(!enabled ? "OFF" : !authorized ? "desautorizado" : blockReason ?? "segurança"); return; }
        var now = _clock.NowMs;
        if (frame is null || now < frame.CapturedAtMs || now - frame.CapturedAtMs > 500 ||
            frame.ReadDurationMs is < 0 or > 500 || !frame.Snapshot.Player.IsAlive)
        { Cancel("dados ausentes/antigos/inválidos"); return; }
        var before = _previous;
        // Trigger evidence needs tight adjacent samples; already-triggered combo keys only need
        // fresh current state and the original source to remain absent (as in Legacy).
        var continuityLimit = State == EarlyPunishState.Executing ? 500 : 100;
        if (before is not null && (frame.CapturedAtMs < before.CapturedAtMs || frame.CapturedAtMs - before.CapturedAtMs > continuityLimit ||
            before.Map != frame.Map || before.Snapshot.Player.Id != frame.Snapshot.Player.Id))
        {
            // A dropped read cannot prove a jump/removal. Re-arm from the fresh buff if it is still
            // present, rather than permanently losing the cycle; never bridge the gap as evidence.
            var stillWatching = frame.CapturedAtMs > before.CapturedAtMs && frame.Map == before.Map &&
                frame.Snapshot.Player.Id == before.Snapshot.Player.Id &&
                SubstitutionBuffs.Active(frame).Count > 0;
            if (State is EarlyPunishState.WatchingBuff or EarlyPunishState.RemovalPending)
                _log.Log(LogLevel.Info, $"NOVO diagnóstico: lacuna {frame.CapturedAtMs - before.CapturedAtMs}ms; buff ainda presente={stillWatching}; sem evidência de gatilho", EventOrigin.Observed);
            Cancel("lacuna de leitura/mapa/personagem; evento não confirmado");
            if (stillWatching)
            {
                _previous = frame;
                _tracker.Update(frame, now, _options.EarlyExpiryMarginMs, 1);
                State = EarlyPunishState.WatchingBuff;
                Status = "NOVO: Sub reobservada após lacuna; aguardando evento novo";
            }
            return;
        }
        _previous = frame;
        var fresh = before?.CapturedAtMs != frame.CapturedAtMs;
        if (fresh && before is not null && SubTeleportCorrelation.Observe(before, frame,
            now - _pendingAt <= SubTeleportCorrelation.WindowMs ? _pending.Select(p => p.Name) : null) is { } jump)
        {
            _jump = jump;
            _log.Log(LogLevel.Info, $"NOVO diagnóstico: deslocamento candidato {jump.Distance:0.00} tiles; dt={frame.CapturedAtMs - before.CapturedAtMs}ms; não é prova de Sub", EventOrigin.Observed);
        }
        if (_jump is { } oldJump && now - oldJump.CapturedAtMs > SubTeleportCorrelation.WindowMs) _jump = null;
        var sample = fresh ? _tracker.Update(frame, now, _options.EarlyExpiryMarginMs, 1) : null;
        if (State == EarlyPunishState.Executing)
        {
            _pending = Array.Empty<EarlyRemoval>(); _jump = null; // consume overlapping/late cycles; never queue combos
            if (!TriggerBuffsRemainAbsent(frame)) { Cancel("buff disparador reapareceu"); return; }
            DispatchNext(); return;
        }
        if (sample?.Ready.Count > 0)
        {
            _pending = sample.Ready; _pendingAt = frame.CapturedAtMs;
            _log.Log(LogLevel.Info, $"NOVO diagnóstico: remoção antecipada de {string.Join(" + ", _pending.Select(p => p.Name))}; âncora={_jump is not null}", EventOrigin.Observed);
        }
        if (_pending.Count > 0)
        {
            if (_pending.Any(p => SubstitutionBuffs.Active(frame).Any(b => b.Name == p.Name)))
            { Cancel("ausência transitória; buff reapareceu"); return; }
            if (_jump is { } anchor && Math.Abs(anchor.CapturedAtMs - _pendingAt) <= SubTeleportCorrelation.WindowMs &&
                now - _pendingAt <= SubTeleportCorrelation.WindowMs)
            {
                var correlated = _pending.Where(p => anchor.Sources.Contains(p.Name, StringComparer.Ordinal)).ToArray();
                if (correlated.Length > 0)
                {
                    _sources.Clear(); foreach (var p in correlated) _sources.Add(p.Name);
                    _pending = Array.Empty<EarlyRemoval>(); _jump = null;
                    _nextKey = 0; _lastKeyAt = -1; _readyAt = now + _options.TriggerDelayMs;
                    State = EarlyPunishState.Executing;
                    _log.Log(LogLevel.Warning, $"NOVO EXPERIMENTAL: remoção antecipada [{string.Join(" + ", _sources)}] + salto local {anchor.Distance:0.00} tiles; uma amostra ausente correlacionada, sem espera extra de segunda ausência. Correção/dash + cancelamento ainda podem simular o evento; ativação NÃO confirmada pelo servidor.", EventOrigin.Observed);
                    DispatchNext(); return;
                }
            }
            if (now - _pendingAt > SubTeleportCorrelation.WindowMs)
            {
                _pending = Array.Empty<EarlyRemoval>(); State = EarlyPunishState.Expired;
                Status = "NOVO: remoção sem deslocamento correlacionado — nenhuma tecla";
                _log.Log(LogLevel.Info, Status, EventOrigin.Observed);
                return;
            }
            State = EarlyPunishState.RemovalPending;
            Status = "NOVO: remoção antecipada; aguardando âncora de deslocamento (até 120ms)"; return;
        }
        if (sample is not null)
        {
            if (sample.NewCycle) _finishedAt = -1;
            if (sample.Watching) { State = EarlyPunishState.WatchingBuff; Status = "NOVO: Sub observada; aguardando consumo + salto local"; }
            else if (sample.Expired)
            {
                State = EarlyPunishState.Expired; Status = "NOVO: fim natural/limítrofe — nenhuma tecla";
                _log.Log(LogLevel.Info, Status, EventOrigin.Observed);
            }
        }
    }
    private void DispatchNext()
    {
        var now = _clock.NowMs;
        while (_nextKey < _options.ComboKeys.Length && _options.ComboKeys[_nextKey].Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            var due = NextKeyDueAtMs;
            if (due is { } at && now < at) return;
            _nextKey++;
            _lastKeyAt = now;
            if (_nextKey == _options.ComboKeys.Length)
            {
                State = EarlyPunishState.Completed; _finishedAt = now;
                Status = "NOVO: combo sem teclas restantes (null); nenhum efeito confirmado";
                return;
            }
        }
        if (_nextKey == 0 && now < _readyAt) { Status = $"NOVO DEBUG: aguardando {_readyAt - now}ms"; return; }
        if (now - _readyAt > _options.SequenceTimeoutMs) { Cancel("timeout do combo"); return; }
        var delay = _nextKey == 1 ? _options.FirstToSecondMs ?? _options.KeyIntervalMs : _options.SecondToThirdMs ?? _options.KeyIntervalMs;
        if (_lastKeyAt >= 0 && now - _lastKeyAt < delay) return;
        ActionResult result;
        try { result = _executor.Execute(Actions[_nextKey], _options.ComboKeys[_nextKey]); }
        catch (Exception ex) { Cancel($"executor: {ex.Message}"); return; }
        if (!result.Accepted)
        {
            if (result.Retryable) { Status = $"NOVO aguardando tecla/giro: {result.Reason}"; return; }
            Cancel(result.Reason ?? "input recusado"); return;
        }
        _nextKey++; _lastKeyAt = _clock.NowMs;
        Status = $"NOVO: {_options.ComboKeys[_nextKey - 1]} enviada ({_nextKey}/3); efeito não confirmado";
        if (_nextKey == 3)
        {
            State = EarlyPunishState.Completed; _finishedAt = _clock.NowMs;
            Status = $"NOVO: {string.Join(" → ", _options.ComboKeys)} enviada; efeitos não confirmados";
            _log.Log(LogLevel.Info, Status, EventOrigin.Executed);
        }
        else if ((_nextKey == 1 ? _options.FirstToSecondMs ?? _options.KeyIntervalMs : _options.SecondToThirdMs ?? _options.KeyIntervalMs) == 0) DispatchNext();
    }
}
