using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Events;
using NinOnlineAutoAssist.Core.Logging;

namespace NinOnlineAutoAssist.Live;

public enum EarlyPunishState { Idle, WatchingBuff, RemovalPending, Executing, Completed, Expired, Cancelled }

public sealed record EarlyPunishOptions
{
    public long EarlyExpiryMarginMs { get; init; } = 1000;
    public int AbsentSamplesRequired { get; init; } = 2;
    public long KeyIntervalMs { get; init; } = 120;
    public string[] ComboKeys { get; init; } = new[] { "A", "Q", "S" };
    public long? FirstToSecondMs { get; init; }
    public long TriggerDelayMs { get; init; } = 0;
    public long? SecondToThirdMs { get; init; }
    public long SequenceTimeoutMs { get; init; } = 1500;
}

/// <summary>
/// Explicit heuristic requested by the operator: early buff removal -> A/Q/S, with no turn or teleport requirement.
/// Removal is NOT proof of a substitution trigger; manual cancellation/dispels can cause false positives.
/// No orientation controller is accepted, so this flow cannot request a turn.
/// </summary>
public sealed class EarlyBuffPunishEngine : ILivePunishEngine
{
    private static readonly string[] ActionIds = { "basic_attack", "followup_hotbar_Q", "followup_hotbar_S" };
    private readonly IActionExecutor _executor;
    private readonly IMonotonicClock _clock;
    private readonly IAutoAssistLogger _log;
    private readonly EarlyPunishOptions _options;
    private LiveFrame? _previous;
    private readonly SubstitutionRemovalTracker _tracker = new();
    private readonly HashSet<string> _triggerSources = new(StringComparer.Ordinal);
    private int _nextKey;
    private long _sequenceReadyAt, _lastKeyAt = -1, _finishedAt = -1;
    public EarlyPunishState State { get; private set; }
    public string Status { get; private set; } = "Aguardando buff; remoção antecipada é uma HEURÍSTICA";
    public long? EstimatedRemainingAtRemovalMs { get; private set; }
    public IReadOnlyList<string> TriggerSources => _triggerSources.ToArray();
    public bool TriggerBuffsRemainAbsent(LiveFrame frame) => _triggerSources.Count > 0 &&
        !SubstitutionBuffs.Active(frame).Any(b => _triggerSources.Contains(b.Name));
    public bool HasUnprocessedRemoval(LiveFrame? frame)
    {
        if (frame is null || _previous is not { } before || frame.CapturedAtMs <= before.CapturedAtMs) return false;
        var current = SubstitutionBuffs.Active(frame);
        return SubstitutionBuffs.Active(before).Any(old => !current.Any(buff => buff.Name == old.Name));
    }
    public long? NextKeyDueAtMs => State != EarlyPunishState.Executing ? null : _nextKey switch
    {
        0 => _sequenceReadyAt,
        1 => _lastKeyAt + (_options.FirstToSecondMs ?? _options.KeyIntervalMs),
        _ => _lastKeyAt + (_options.SecondToThirdMs ?? _options.KeyIntervalMs)
    };
    // Do not begin a blocking turn tap when a combo key is due or imminent.
    public bool HasTimeForTurn => NextKeyDueAtMs is not { } due || due - _clock.NowMs > 60;
    public bool BlocksAutoTurning => State is EarlyPunishState.RemovalPending or EarlyPunishState.Executing ||
        (_finishedAt >= 0 && _clock.NowMs - _finishedAt < _options.KeyIntervalMs);

    public EarlyBuffPunishEngine(IActionExecutor executor, IMonotonicClock clock, EarlyPunishOptions? options = null,
        IAutoAssistLogger? logger = null)
    {
        _executor = executor;
        _clock = clock;
        _log = logger ?? NullLogger.Instance;
        _options = options ?? new EarlyPunishOptions();
        if (_options.TriggerDelayMs is < 0 or > 10_000)
            throw new ArgumentException("Trigger delay must be between 0 and 10000 ms.");
        if (_options.EarlyExpiryMarginMs is < 0 or > 60_000 || _options.AbsentSamplesRequired is < 2 or > 10 ||
            _options.KeyIntervalMs is < 40 or > 2000 || _options.SequenceTimeoutMs < _options.KeyIntervalMs * 3)
            throw new ArgumentException("Invalid early-removal heuristic options: margin >= 0, >= 2 absent samples, interval >= 40 ms and timeout >= 3 intervals required.");
        if (_options.ComboKeys is null || _options.ComboKeys.Length != 3 || _options.ComboKeys.Any(string.IsNullOrWhiteSpace) ||
            _options.FirstToSecondMs is < 0 or > 2000 || _options.SecondToThirdMs is < 0 or > 2000 ||
            (_options.FirstToSecondMs ?? _options.KeyIntervalMs) + (_options.SecondToThirdMs ?? _options.KeyIntervalMs) >= _options.SequenceTimeoutMs)
            throw new ArgumentException("Invalid combo keys or transition delays.");
        _options = _options with { ComboKeys = _options.ComboKeys.ToArray() };
    }

    public void Reset()
    {
        _previous = null; _tracker.Reset(); _triggerSources.Clear();
        _nextKey = 0;
        _lastKeyAt = _finishedAt = -1;
        _sequenceReadyAt = 0;
        EstimatedRemainingAtRemovalMs = null;
        State = EarlyPunishState.Idle;
        Status = "Aguardando buff; remoção antecipada é uma HEURÍSTICA";
    }

    private void Cancel(string reason)
    {
        var wasTracking = _previous is not null || State is EarlyPunishState.RemovalPending or EarlyPunishState.Executing;
        var interruptedSequence = State is EarlyPunishState.RemovalPending or EarlyPunishState.Executing;
        Reset();
        if (interruptedSequence) _finishedAt = _clock.NowMs;
        State = EarlyPunishState.Cancelled;
        Status = $"Cancelado: {reason}";
        if (wasTracking) _log.Log(LogLevel.Warning, Status, EventOrigin.Observed);
    }

    public void Update(LiveFrame? frame, bool enabled, bool authorized, bool safetyAllowed, string? blockReason = null)
    {
        if (!enabled || !authorized || !safetyAllowed)
        {
            Cancel(!enabled ? "módulo OFF" : !authorized ? "Combo desautorizado" : blockReason ?? "bloqueio de segurança");
            return;
        }
        var now = _clock.NowMs;
        if (frame is null || now < frame.CapturedAtMs || now - frame.CapturedAtMs > 500 || frame.ReadDurationMs is < 0 or > 500)
        { Cancel("amostra ausente/antiga; não é desaparecimento do buff"); return; }
        if (!frame.Snapshot.Player.IsAlive)
        { Cancel("jogador inválido"); return; }
        var before = _previous;
        if (before is not null)
        {
            if (frame.CapturedAtMs < before.CapturedAtMs || frame.CapturedAtMs - before.CapturedAtMs > 500 ||
                before.Map != frame.Map || before.Snapshot.Player.Id != frame.Snapshot.Player.Id)
            { Cancel("continuidade, mapa ou identidade alterada"); return; }
        }
        _previous = frame;

        // Count each buff separately and consume simultaneous/late triggers once, never queue combos.
        var sample = before?.CapturedAtMs == frame.CapturedAtMs ? null
            : _tracker.Update(frame, now, _options.EarlyExpiryMarginMs, _options.AbsentSamplesRequired);

        if (State == EarlyPunishState.Executing)
        {
            if (!TriggerBuffsRemainAbsent(frame)) { Cancel("buff que disparou o combo reapareceu; restante cancelado"); return; }
            DispatchNext();
            return;
        }
        if (sample is null) return; // evidence still requires DISTINCT samples
        if (sample.NewCycle)
        {
            _nextKey = 0; _lastKeyAt = _finishedAt = -1;
            EstimatedRemainingAtRemovalMs = null;
        }
        if (sample.Ready.Count == 0)
        {
            if (sample.PendingSamples > 0)
            {
                State = EarlyPunishState.RemovalPending;
                Status = $"HEURÍSTICA: ausência {sample.PendingSamples}/{_options.AbsentSamplesRequired} por buff";
            }
            else if (sample.Watching)
            {
                State = EarlyPunishState.WatchingBuff;
                Status = sample.KnownDuration ? "Subs observadas separadamente — aguardando remoção antecipada"
                    : "Buff observado, duração desconhecida — heurística bloqueada";
            }
            else if (sample.Expired)
            {
                State = EarlyPunishState.Expired;
                Status = "Fim normal/limítrofe da contagem — nenhuma tecla enviada";
                _log.Log(LogLevel.Info, Status, EventOrigin.Observed);
            }
            return;
        }
        var remaining = sample.Ready.Min(b => b.RemainingMs);
        _triggerSources.Clear();
        foreach (var buff in sample.Ready) _triggerSources.Add(buff.Name);
        EstimatedRemainingAtRemovalMs = remaining;
        // Debug delay starts at CONFIRMED early disappearance, before the first key.
        // It does not consume the timeout reserved for the actual combo.
        _sequenceReadyAt = now + _options.TriggerDelayMs;
        _nextKey = 0; _lastKeyAt = -1;
        State = EarlyPunishState.Executing;
        _log.Log(LogLevel.Warning, $"HEURÍSTICA de remoção antecipada [{string.Join(" + ", _triggerSources)}]: {remaining} ms estimados restantes; atraso inicial {_options.TriggerDelayMs} ms; combo {string.Join(" → ", _options.ComboKeys)} sem alvo obrigatório; giro prioritário quando permitido pelo adaptador. Cancelamento/dispell também pode remover o buff; trigger NÃO confirmado.", EventOrigin.Observed);
        DispatchNext();
    }

    private void DispatchNext()
    {
        var now = _clock.NowMs;
        if (_nextKey == 0 && now < _sequenceReadyAt)
        {
            Status = $"DEBUG: aguardando {_sequenceReadyAt - now} ms após remoção confirmada antes de {_options.ComboKeys[0]}";
            return;
        }
        if (now - _sequenceReadyAt > _options.SequenceTimeoutMs) { Cancel("tempo da sequência esgotado"); return; }
        var delay = _nextKey == 1 ? _options.FirstToSecondMs ?? _options.KeyIntervalMs
            : _options.SecondToThirdMs ?? _options.KeyIntervalMs;
        if (_lastKeyAt >= 0 && now - _lastKeyAt < delay) return;
        var action = (Id: ActionIds[_nextKey], Key: _options.ComboKeys[_nextKey]);
        ActionResult result;
        try { result = _executor.Execute(action.Id, action.Key); } // adapter independently rechecks the gate
        catch (Exception ex) { Cancel($"executor falhou na tecla {action.Key}: {ex.Message}"); return; }
        if (!result.Accepted)
        {
            if (result.Retryable) { Status = $"Aguardando liberar {action.Key}: {result.Reason}"; return; }
            Cancel($"tecla {action.Key} recusada: {result.Reason}"); return;
        }
        _nextKey++;
        _lastKeyAt = _clock.NowMs;
        Status = $"HEURÍSTICA: {action.Key} enviada ({_nextKey}/3); efeito não confirmado";
        if (_nextKey == _options.ComboKeys.Length)
        {
            State = EarlyPunishState.Completed;
            _finishedAt = _clock.NowMs;
            Status = $"{string.Join(" → ", _options.ComboKeys)} enviada; efeitos/acertos não confirmados";
            _log.Log(LogLevel.Info, Status, EventOrigin.Executed);
        }
        else if ((_nextKey == 1 ? _options.FirstToSecondMs ?? _options.KeyIntervalMs : _options.SecondToThirdMs ?? _options.KeyIntervalMs) == 0)
            DispatchNext(); // no extra wait after key-up; adapter rechecks every key independently
    }
}
