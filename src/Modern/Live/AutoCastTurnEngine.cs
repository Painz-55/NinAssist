using System;
using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Geometry;
using NinOnlineAutoAssist.Core.Models;

namespace NinOnlineAutoAssist.Live;

public sealed class AutoCastTurnEngine
{
	private readonly IMonotonicClock _clock;

	private readonly AutoCastTurnSettings _settings;

	private readonly Func<Direction, TurnResult> _turn;

	private static readonly DirectionCalculatorOptions Geometry = new DirectionCalculatorOptions
	{
		PositionTolerance = 0.1,
		HysteresisRatio = 1.25
	};

	private bool _sawIdle;

	private bool _consumed;

	private bool _sawEarlierProgress;

	private long _lastCapture = -1L;

	private long _previousRemaining;

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
	{
		settings.Validate();
		_clock = clock;
		_settings = settings;
		_turn = turn;
	}

	public void Reset()
	{
		bool sawEarlierProgress = (InFinalWindow = false);
		_sawIdle = (_consumed = (_sawEarlierProgress = sawEarlierProgress));
		_lastCapture = -1L;
		_samples = 0;
		CastTurnRequest tracking = (Dispatching = null);
		_tracking = tracking;
		_lastTurnAt = (_lastReleaseAt = null);
		Status = "Auto Cast Turn: aguardando novo cast completo";
	}

	public static bool Fresh(LiveFrame? frame, long now)
	{
		if ((object)frame != null && now >= frame.CapturedAtMs && now - frame.CapturedAtMs <= 350)
		{
			long readDurationMs = frame.ReadDurationMs;
			if (readDurationMs >= 0)
			{
				return readDurationMs <= 350;
			}
			return false;
		}
		return false;
	}

	private long Window(CastObservation cast)
	{
		return Math.Min(_settings.LeadTimeMs, cast.TotalMs / 4);
	}

	private static bool TimingValid(LiveFrame frame, CastObservation cast, long now)
	{
		long? observedAtMs = cast.ObservedAtMs;
		if (observedAtMs.HasValue)
		{
			long valueOrDefault = observedAtMs.GetValueOrDefault();
			if (valueOrDefault >= frame.CapturedAtMs && valueOrDefault <= frame.CapturedAtMs + frame.ReadDurationMs)
			{
				return valueOrDefault <= now;
			}
			return false;
		}
		return true;
	}

	private static long Remaining(LiveFrame frame, CastObservation cast, long now)
	{
		return cast.RemainingMs - (now - (cast.ObservedAtMs ?? frame.CapturedAtMs));
	}

	private static Direction Desired(LiveFrame frame)
	{
		TargetInfo target = frame.Snapshot.Target;
		if ((object)target == null || !target.IsAlive || !target.IsTargeted)
		{
			return Direction.None;
		}
		PlayerState player = frame.Snapshot.Player;
		double num = target.PositionX - player.PositionX;
		double num2 = target.PositionY - player.PositionY;
		if (!double.IsFinite(num) || !double.IsFinite(num2) || player.Facing == Direction.None)
		{
			return Direction.None;
		}
		return DirectionCalculator.Calculate(num, num2, player.Facing, Geometry);
	}

	public bool CanDispatch(LiveFrame? frame, long now)
	{
		return CheckDispatch(frame, now, maintaining: false);
	}

	public bool CanMaintain(LiveFrame? frame, long now)
	{
		return CheckDispatch(frame, now, maintaining: true);
	}

	private bool CheckDispatch(LiveFrame? frame, long now, bool maintaining)
	{
		if (Fresh(frame, now))
		{
			CastTurnRequest dispatching = Dispatching;
			if ((object)dispatching != null)
			{
				CastObservation cast = frame.Cast;
				if ((object)cast != null && cast.Active && cast.Valid && frame.Map == dispatching.Map && !(frame.Snapshot.Player.Id != dispatching.PlayerId) && frame.Snapshot.Player.IsAlive && !(frame.Snapshot.Target?.Id != dispatching.TargetId) && cast.SpellId == dispatching.SpellId && _settings.Allows(cast.SpellId) && cast.StartedAtGameMs == dispatching.StartedAtGameMs && cast.TotalMs == dispatching.TotalMs)
				{
					if (!TimingValid(frame, cast, now))
					{
						return false;
					}
					long num = Remaining(frame, cast, now);
					if ((maintaining ? (num < -70) : (num <= 0)) || num > Window(cast) || Desired(frame) != dispatching.Direction)
					{
						return false;
					}
					if (maintaining)
					{
						long? lastTurnAt = _lastTurnAt;
						if (lastTurnAt.HasValue)
						{
							long valueOrDefault = lastTurnAt.GetValueOrDefault();
							if (frame.CapturedAtMs > valueOrDefault + 40)
							{
								return frame.Snapshot.Player.Facing == dispatching.Direction;
							}
						}
					}
					return true;
				}
			}
		}
		return false;
	}

	public void Update(LiveFrame? frame, bool allowed, bool higherPriorityBusy, bool enabledWhileBlocked = false)
	{
		long nowMs = _clock.NowMs;
		InFinalWindow = false;
		if (!allowed)
		{
			Reset();
			// An already-enabled module may observe a verified idle cast while a transient
			// safety gate is closed. It still cannot dispatch until every gate passes.
			if (enabledWhileBlocked && Fresh(frame, nowMs) && frame.Snapshot.Player.IsAlive &&
				!string.IsNullOrEmpty(frame.Snapshot.Player.Id) && frame.Cast is { Valid: true, Active: false } idle &&
				TimingValid(frame, idle, nowMs))
			{
				_contextPlayer = frame.Snapshot.Player.Id;
				_contextMap = frame.Map;
				_sawIdle = true;
			}
			Status = "Auto Cast Turn bloqueado/OFF";
			return;
		}
		if (!Fresh(frame, nowMs))
		{
			if (_lastCapture >= 0 && nowMs >= _lastCapture && nowMs - _lastCapture <= 400)
			{
				Status = "Auto Cast Turn: leitura temporariamente indisponível; sem giro";
				return;
			}
			Reset();
			Status = "Auto Cast Turn: lacuna de leitura; aguardando novo cast";
			return;
		}
		CastObservation cast = frame.Cast;
		if ((object)cast == null || !cast.Valid || !frame.Snapshot.Player.IsAlive || !TimingValid(frame, cast, nowMs))
		{
			Reset();
			Status = "Auto Cast Turn: cast desconhecido/dados antigos";
			return;
		}
		if (_contextPlayer != frame.Snapshot.Player.Id || _contextMap != frame.Map)
		{
			Reset();
			_contextPlayer = frame.Snapshot.Player.Id;
			_contextMap = frame.Map;
			_lastStarted = 0L;
		}
		if (_lastCapture >= 0 && (frame.CapturedAtMs < _lastCapture || frame.CapturedAtMs - _lastCapture > 400))
		{
			Reset();
			Status = "Auto Cast Turn: lacuna de leitura; aguardando novo cast";
		}
		bool flag = frame.CapturedAtMs > _lastCapture;
		_lastCapture = frame.CapturedAtMs;
		if (!cast.Active)
		{
			_sawIdle = true;
			_tracking = null;
			_consumed = (_sawEarlierProgress = false);
			_samples = 0;
			_lastTurnAt = (_lastReleaseAt = null);
			Status = "Auto Cast Turn: aguardando conjuração";
			return;
		}
		if (!_sawIdle)
		{
			Status = "Auto Cast Turn: aguardando fim do cast atual para armar o próximo";
			return;
		}
		if (!_settings.Allows(cast.SpellId))
		{
			_tracking = null;
			_consumed = true;
			Status = $"Auto Cast Turn: jutsu ID {cast.SpellId} excluído; sem giro";
			return;
		}
		TargetInfo target = frame.Snapshot.Target;
		if ((object)_tracking == null || _tracking.SpellId != cast.SpellId || _tracking.StartedAtGameMs != cast.StartedAtGameMs)
		{
			if (cast.StartedAtGameMs <= _lastStarted)
			{
				Status = "Auto Cast Turn: marcador de cast repetido/antigo; descartado";
				return;
			}
			_lastStarted = cast.StartedAtGameMs;
			_tracking = new CastTurnRequest(frame.Map, frame.Snapshot.Player.Id, target?.Id ?? "", cast.SpellId, cast.StartedAtGameMs, cast.TotalMs, Direction.None);
			_consumed = false;
			_sawEarlierProgress = cast.RemainingMs > Window(cast);
			_samples = 0;
			_previousRemaining = cast.RemainingMs;
			_lastTurnAt = (_lastReleaseAt = null);
		}
		if (flag)
		{
			_samples++;
		}
		if (frame.Map != _tracking.Map || frame.Snapshot.Player.Id != _tracking.PlayerId || (object)target == null || !target.IsAlive || !target.IsTargeted || target.Id != _tracking.TargetId || cast.TotalMs != _tracking.TotalMs || cast.RemainingMs > _previousRemaining + 50 || higherPriorityBusy)
		{
			_consumed = true;
			Status = "Auto Cast Turn: cast descartado por alvo/contexto/prioridade";
			return;
		}
		if (flag)
		{
			_previousRemaining = cast.RemainingMs;
		}
		long num = Remaining(frame, cast, nowMs);
		InFinalWindow = num > 0 && num <= Window(cast);
		if (_consumed)
		{
			return;
		}
		Status = $"Auto Cast Turn: cast {cast.SpellId} · {Math.Max(0L, num)} ms restantes";
		if (num <= 0)
		{
			_consumed = true;
			Status = "Auto Cast Turn: janela final perdida; sem giro atrasado";
		}
		else
		{
			if (!InFinalWindow || !_sawEarlierProgress || _samples < 2)
			{
				return;
			}
			Direction direction = Desired(frame);
			if (direction == Direction.None)
			{
				Status = "Auto Cast Turn: direção cardinal/alvo não observados; sem giro";
				return;
			}
			if (direction != frame.Snapshot.Player.Facing)
			{
				long? lastTurnAt = _lastTurnAt;
				if (lastTurnAt.HasValue)
				{
					long valueOrDefault = lastTurnAt.GetValueOrDefault();
					if (nowMs - valueOrDefault < 40 || frame.CapturedAtMs <= valueOrDefault)
					{
						return;
					}
				}
				lastTurnAt = _lastReleaseAt;
				if (lastTurnAt.HasValue)
				{
					long valueOrDefault2 = lastTurnAt.GetValueOrDefault();
					if (nowMs - valueOrDefault2 < 20)
					{
						return;
					}
				}
				_lastTurnAt = nowMs;
				Dispatching = _tracking with
				{
					Direction = direction
				};
				try
				{
					TurnResult turnResult = _turn(direction);
					if (!turnResult.Accepted)
					{
						_consumed = true;
					}
					Status = (turnResult.Accepted ? $"Auto Cast Turn: mantendo/corrigindo {direction} até o fim do cast; efeito NÃO confirmado" : ("Auto Cast Turn: giro recusado — " + turnResult.Reason));
					return;
				}
				finally
				{
					Dispatching = null;
					_lastReleaseAt = _clock.NowMs;
				}
			}
			Status = "Auto Cast Turn: alinhado; vigiando direção até o fim do cast";
		}
	}
}
