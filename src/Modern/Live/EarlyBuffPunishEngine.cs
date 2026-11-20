using System;
using System.Collections.Generic;
using System.Linq;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Events;
using NinOnlineAutoAssist.Core.Logging;

namespace NinOnlineAutoAssist.Live;

public sealed class EarlyBuffPunishEngine : ILivePunishEngine
{
	private static readonly string[] ActionIds = new string[3] { "basic_attack", "followup_hotbar_Q", "followup_hotbar_S" };

	private readonly IActionExecutor _executor;

	private readonly IMonotonicClock _clock;

	private readonly IAutoAssistLogger _log;

	private readonly EarlyPunishOptions _options;

	private readonly HashSet<string>? _eligibleSources;

	private LiveFrame? _previous;

	private readonly SubstitutionRemovalTracker _tracker = new SubstitutionRemovalTracker();

	private readonly HashSet<string> _triggerSources = new HashSet<string>(StringComparer.Ordinal);

	private int _nextKey;

	private long _sequenceReadyAt;

	private long _lastKeyAt = -1L;

	private long _finishedAt = -1L;

	public EarlyPunishState State { get; private set; }

	public string Status { get; private set; } = "Aguardando buff; remoção antecipada é uma HEURÍSTICA";

	public long? EstimatedRemainingAtRemovalMs { get; private set; }

	public IReadOnlyList<string> TriggerSources => _triggerSources.ToArray();

	public long? NextKeyDueAtMs => (State == EarlyPunishState.Executing) ? new long?(_nextKey switch
	{
		0 => _sequenceReadyAt,
		1 => _lastKeyAt + (_options.FirstToSecondMs ?? _options.KeyIntervalMs),
		_ => _lastKeyAt + (_options.SecondToThirdMs ?? _options.KeyIntervalMs),
	}) : ((long?)null);

	public bool HasTimeForTurn
	{
		get
		{
			long? nextKeyDueAtMs = NextKeyDueAtMs;
			if (nextKeyDueAtMs.HasValue)
			{
				long valueOrDefault = nextKeyDueAtMs.GetValueOrDefault();
				return valueOrDefault - _clock.NowMs > 60;
			}
			return true;
		}
	}

	public bool BlocksAutoTurning
	{
		get
		{
			EarlyPunishState state = State;
			if ((uint)(state - 2) > 1u)
			{
				if (_finishedAt >= 0)
				{
					return _clock.NowMs - _finishedAt < _options.KeyIntervalMs;
				}
				return false;
			}
			return true;
		}
	}

	private bool Eligible(string name)
	{
		return _eligibleSources?.Contains(name) ?? true;
	}

	private IReadOnlyList<ObservedBuff> EligibleBuffs(LiveFrame frame)
	{
		return (from b in SubstitutionBuffs.Active(frame)
			where Eligible(b.Name)
			select b).ToArray();
	}

	public bool TriggerBuffsRemainAbsent(LiveFrame frame)
	{
		if (_triggerSources.Count > 0)
		{
			return !EligibleBuffs(frame).Any((ObservedBuff b) => _triggerSources.Contains(b.Name));
		}
		return false;
	}

	public bool HasUnprocessedRemoval(LiveFrame? frame)
	{
		if ((object)frame != null)
		{
			LiveFrame previous = _previous;
			if ((object)previous != null && frame.CapturedAtMs > previous.CapturedAtMs)
			{
				IReadOnlyList<ObservedBuff> current = EligibleBuffs(frame);
				return EligibleBuffs(previous).Any((ObservedBuff old) => !current.Any((ObservedBuff buff) => buff.Name == old.Name));
			}
		}
		return false;
	}

	public EarlyBuffPunishEngine(IActionExecutor executor, IMonotonicClock clock, EarlyPunishOptions? options = null, IAutoAssistLogger? logger = null)
	{
		_executor = executor;
		_clock = clock;
		_log = logger ?? NullLogger.Instance;
		_options = options ?? new EarlyPunishOptions();
		long triggerDelayMs = _options.TriggerDelayMs;
		if ((triggerDelayMs < 0 || triggerDelayMs > 10000) ? true : false)
		{
			throw new ArgumentException("Trigger delay must be between 0 and 10000 ms.");
		}
		triggerDelayMs = _options.EarlyExpiryMarginMs;
		bool flag = ((triggerDelayMs < 0 || triggerDelayMs > 60000) ? true : false);
		if (!flag)
		{
			int absentSamplesRequired = _options.AbsentSamplesRequired;
			flag = ((absentSamplesRequired < 2 || absentSamplesRequired > 10) ? true : false);
		}
		bool flag2 = flag;
		if (!flag2)
		{
			long keyIntervalMs = _options.KeyIntervalMs;
			flag2 = ((keyIntervalMs < 40 || keyIntervalMs > 2000) ? true : false);
		}
		if (flag2 || _options.SequenceTimeoutMs < _options.KeyIntervalMs * 3)
		{
			throw new ArgumentException("Invalid early-removal heuristic options: margin >= 0, >= 2 absent samples, interval >= 40 ms and timeout >= 3 intervals required.");
		}
		flag2 = _options.ComboKeys == null || _options.ComboKeys.Length != 3 || _options.ComboKeys.Any(string.IsNullOrWhiteSpace);
		bool flag3;
		if (!flag2)
		{
			long? firstToSecondMs = _options.FirstToSecondMs;
			if (firstToSecondMs.HasValue)
			{
				triggerDelayMs = firstToSecondMs.GetValueOrDefault();
				if (triggerDelayMs < 0 || triggerDelayMs > 2000)
				{
					flag3 = true;
					goto IL_01bf;
				}
			}
			flag3 = false;
			goto IL_01bf;
		}
		goto IL_01c2;
		IL_01c2:
		flag = flag2;
		if (!flag)
		{
			long? firstToSecondMs = _options.SecondToThirdMs;
			if (firstToSecondMs.HasValue)
			{
				triggerDelayMs = firstToSecondMs.GetValueOrDefault();
				if (triggerDelayMs < 0 || triggerDelayMs > 2000)
				{
					flag3 = true;
					goto IL_01fa;
				}
			}
			flag3 = false;
			goto IL_01fa;
		}
		goto IL_01fc;
		IL_01fc:
		if (flag || (_options.FirstToSecondMs ?? _options.KeyIntervalMs) + (_options.SecondToThirdMs ?? _options.KeyIntervalMs) >= _options.SequenceTimeoutMs)
		{
			throw new ArgumentException("Invalid combo keys or transition delays.");
		}
		_options = _options with
		{
			ComboKeys = _options.ComboKeys.ToArray()
		};
		string[] eligibleSources = _options.EligibleSources;
		if (eligibleSources != null)
		{
			if (eligibleSources.Any((string name) => !SubstitutionBuffs.IsPunishEligible(name)) || eligibleSources.Distinct<string>(StringComparer.Ordinal).Count() != eligibleSources.Length)
			{
				throw new ArgumentException("Only distinct Tai/Water buffs may be restricted counterattack sources.");
			}
			_eligibleSources = new HashSet<string>(eligibleSources, StringComparer.Ordinal);
		}
		return;
		IL_01fa:
		flag = flag3;
		goto IL_01fc;
		IL_01bf:
		flag2 = flag3;
		goto IL_01c2;
	}

	public void Reset()
	{
		_previous = null;
		_tracker.Reset();
		_triggerSources.Clear();
		_nextKey = 0;
		_lastKeyAt = (_finishedAt = -1L);
		_sequenceReadyAt = 0L;
		EstimatedRemainingAtRemovalMs = null;
		State = EarlyPunishState.Idle;
		Status = "Aguardando buff; remoção antecipada é uma HEURÍSTICA";
	}

	private void Cancel(string reason)
	{
		bool flag = (object)_previous != null;
		EarlyPunishState state;
		if (!flag)
		{
			state = State;
			bool flag2 = (uint)(state - 2) <= 1u;
			flag = flag2;
		}
		bool flag3 = flag;
		state = State;
		flag = (uint)(state - 2) <= 1u;
		bool num = flag;
		Reset();
		if (num)
		{
			_finishedAt = _clock.NowMs;
		}
		State = EarlyPunishState.Cancelled;
		Status = "Cancelado: " + reason;
		if (flag3)
		{
			_log.Log(LogLevel.Warning, Status, EventOrigin.Observed);
		}
	}

	public void Update(LiveFrame? frame, bool enabled, bool authorized, bool safetyAllowed, string? blockReason = null)
	{
		if (!enabled || !authorized || !safetyAllowed)
		{
			Cancel((!enabled) ? "módulo OFF" : ((!authorized) ? "Combo desautorizado" : (blockReason ?? "bloqueio de segurança")));
			return;
		}
		long nowMs = _clock.NowMs;
		bool flag = (object)frame == null || nowMs < frame.CapturedAtMs || nowMs - frame.CapturedAtMs > 500;
		if (!flag)
		{
			long readDurationMs = frame.ReadDurationMs;
			bool flag2 = ((readDurationMs < 0 || readDurationMs > 500) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			Cancel("amostra ausente/antiga; não é desaparecimento do buff");
			return;
		}
		if (!frame.Snapshot.Player.IsAlive)
		{
			Cancel("jogador inválido");
			return;
		}
		LiveFrame previous = _previous;
		if ((object)previous != null && (frame.CapturedAtMs < previous.CapturedAtMs || frame.CapturedAtMs - previous.CapturedAtMs > 500 || previous.Map != frame.Map || previous.Snapshot.Player.Id != frame.Snapshot.Player.Id))
		{
			Cancel("continuidade, mapa ou identidade alterada");
			return;
		}
		_previous = frame;
		RemovalSample removalSample = ((previous?.CapturedAtMs == frame.CapturedAtMs) ? null : _tracker.Update(frame, nowMs, _options.EarlyExpiryMarginMs, _options.AbsentSamplesRequired, Eligible));
		if (State == EarlyPunishState.Executing)
		{
			if (!TriggerBuffsRemainAbsent(frame))
			{
				Cancel("buff que disparou o combo reapareceu; restante cancelado");
			}
			else
			{
				DispatchNext();
			}
		}
		else
		{
			if ((object)removalSample == null)
			{
				return;
			}
			if (removalSample.NewCycle)
			{
				_nextKey = 0;
				_lastKeyAt = (_finishedAt = -1L);
				EstimatedRemainingAtRemovalMs = null;
			}
			if (removalSample.Ready.Count == 0)
			{
				if (removalSample.PendingSamples > 0)
				{
					State = EarlyPunishState.RemovalPending;
					Status = $"HEURÍSTICA: ausência {removalSample.PendingSamples}/{_options.AbsentSamplesRequired} por buff";
				}
				else if (removalSample.Watching)
				{
					State = EarlyPunishState.WatchingBuff;
					Status = (removalSample.KnownDuration ? "Subs observadas separadamente — aguardando remoção antecipada" : "Buff observado, duração desconhecida — heurística bloqueada");
				}
				else if (removalSample.Expired)
				{
					State = EarlyPunishState.Expired;
					Status = "Fim normal/limítrofe da contagem — nenhuma tecla enviada";
					_log.Log(LogLevel.Info, Status, EventOrigin.Observed);
				}
				return;
			}
			long value = removalSample.Ready.Min((EarlyRemoval b) => b.RemainingMs);
			_triggerSources.Clear();
			foreach (EarlyRemoval item in removalSample.Ready)
			{
				_triggerSources.Add(item.Name);
			}
			EstimatedRemainingAtRemovalMs = value;
			_sequenceReadyAt = nowMs + _options.TriggerDelayMs;
			_nextKey = 0;
			_lastKeyAt = -1L;
			State = EarlyPunishState.Executing;
			_log.Log(LogLevel.Warning, $"HEURÍSTICA de remoção antecipada [{string.Join(" + ", _triggerSources)}]: {value} ms estimados restantes; atraso inicial {_options.TriggerDelayMs} ms; combo {string.Join(" → ", _options.ComboKeys)} sem alvo obrigatório; giro prioritário quando permitido pelo adaptador. Cancelamento/dispell também pode remover o buff; trigger NÃO confirmado.", EventOrigin.Observed);
			DispatchNext();
		}
	}

	private void DispatchNext()
	{
		long nowMs = _clock.NowMs;
		while (_nextKey < _options.ComboKeys.Length && _options.ComboKeys[_nextKey].Equals("null", StringComparison.OrdinalIgnoreCase))
		{
			long? nextKeyDueAtMs = NextKeyDueAtMs;
			if (nextKeyDueAtMs.HasValue)
			{
				long valueOrDefault = nextKeyDueAtMs.GetValueOrDefault();
				if (nowMs < valueOrDefault)
				{
					return;
				}
			}
			_nextKey++;
			_lastKeyAt = nowMs;
			if (_nextKey == _options.ComboKeys.Length)
			{
				State = EarlyPunishState.Completed;
				_finishedAt = nowMs;
				Status = "Combo sem teclas restantes (null); nenhum efeito confirmado";
				return;
			}
		}
		if (_nextKey == 0 && nowMs < _sequenceReadyAt)
		{
			Status = $"DEBUG: aguardando {_sequenceReadyAt - nowMs} ms após remoção confirmada antes de {_options.ComboKeys[0]}";
			return;
		}
		if (nowMs - _sequenceReadyAt > _options.SequenceTimeoutMs)
		{
			Cancel("tempo da sequência esgotado");
			return;
		}
		long num = ((_nextKey != 1) ? (_options.SecondToThirdMs ?? _options.KeyIntervalMs) : (_options.FirstToSecondMs ?? _options.KeyIntervalMs));
		if (_lastKeyAt >= 0 && nowMs - _lastKeyAt < num)
		{
			return;
		}
		(string, string) tuple = (ActionIds[_nextKey], _options.ComboKeys[_nextKey]);
		ActionResult actionResult;
		try
		{
			actionResult = _executor.Execute(tuple.Item1, tuple.Item2);
		}
		catch (Exception ex)
		{
			Cancel("executor falhou na tecla " + tuple.Item2 + ": " + ex.Message);
			return;
		}
		if (!actionResult.Accepted)
		{
			if (actionResult.Retryable)
			{
				Status = "Aguardando liberar " + tuple.Item2 + ": " + actionResult.Reason;
			}
			else
			{
				Cancel("tecla " + tuple.Item2 + " recusada: " + actionResult.Reason);
			}
			return;
		}
		_nextKey++;
		_lastKeyAt = _clock.NowMs;
		Status = $"HEURÍSTICA: {tuple.Item2} enviada ({_nextKey}/3); efeito não confirmado";
		if (_nextKey == _options.ComboKeys.Length)
		{
			State = EarlyPunishState.Completed;
			_finishedAt = _clock.NowMs;
			Status = string.Join(" → ", _options.ComboKeys) + " enviada; efeitos/acertos não confirmados";
			_log.Log(LogLevel.Info, Status, EventOrigin.Executed);
		}
		else if (((_nextKey != 1) ? (_options.SecondToThirdMs ?? _options.KeyIntervalMs) : (_options.FirstToSecondMs ?? _options.KeyIntervalMs)) == 0L)
		{
			DispatchNext();
		}
	}
}
