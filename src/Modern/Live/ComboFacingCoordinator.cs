using System;
using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Events;
using NinOnlineAutoAssist.Core.Geometry;
using NinOnlineAutoAssist.Core.Logging;
using NinOnlineAutoAssist.Core.Models;

namespace NinOnlineAutoAssist.Live;

public sealed class ComboFacingCoordinator
{
	private readonly IOrientationController _orientation;

	private readonly IMonotonicClock _clock;

	private readonly Func<LiveFrame?> _latest;

	private readonly Func<bool> _turnAllowed;

	private readonly DirectionCalculatorOptions _geometry;

	private readonly IAutoAssistLogger _log;

	private long? _lastTurnAt;

	private string? _turnTargetId;

	public ComboFacingCoordinator(IOrientationController orientation, IMonotonicClock clock, Func<LiveFrame?> latest, Func<bool> turnAllowed, DirectionCalculatorOptions? geometry = null, IAutoAssistLogger? logger = null)
	{
		_orientation = orientation;
		_clock = clock;
		_latest = latest;
		_turnAllowed = turnAllowed;
		_geometry = geometry ?? new DirectionCalculatorOptions
		{
			PositionTolerance = 0.1,
			HysteresisRatio = 1.25
		};
		_log = logger ?? NullLogger.Instance;
	}

	public void Reset()
	{
		_lastTurnAt = null;
		_turnTargetId = null;
	}

	public ActionResult BeforeAction()
	{
		LiveFrame liveFrame = _latest();
		long nowMs = _clock.NowMs;
		bool flag = (object)liveFrame == null || nowMs < liveFrame.CapturedAtMs || nowMs - liveFrame.CapturedAtMs > 500;
		if (!flag)
		{
			long readDurationMs = liveFrame.ReadDurationMs;
			bool flag2 = ((readDurationMs < 0 || readDurationMs > 500) ? true : false);
			flag = flag2;
		}
		if (flag || !liveFrame.Snapshot.Player.IsAlive)
		{
			return ActionResult.Rejected("Sem leitura válida para preparar giro do combo");
		}
		TargetInfo target = liveFrame.Snapshot.Target;
		if ((object)target == null || !target.IsAlive || !target.IsTargeted)
		{
			Reset();
			return ActionResult.Ok("Combo independente: sem alvo de giro");
		}
		if (!_turnAllowed())
		{
			return ActionResult.Rejected("Giro preparatório do combo sem permissão");
		}
		PlayerState player = liveFrame.Snapshot.Player;
		Direction direction = DirectionCalculator.Calculate(target.PositionX - player.PositionX, target.PositionY - player.PositionY, player.Facing, _geometry);
		if (direction == Direction.None || player.Facing == Direction.None)
		{
			return ActionResult.Rejected("Direção do combo indisponível; tecla bloqueada");
		}
		long? lastTurnAt = _lastTurnAt;
		if (lastTurnAt.HasValue)
		{
			long valueOrDefault = lastTurnAt.GetValueOrDefault();
			if (target.Id != _turnTargetId)
			{
				return ActionResult.Rejected("Alvo mudou após giro preparatório; tecla bloqueada");
			}
			if (liveFrame.CapturedAtMs <= valueOrDefault || direction != player.Facing)
			{
				return ActionResult.Deferred("Aguardando direção observada após giro; não repetir tecla de giro");
			}
			Reset();
			_log.Log(LogLevel.Info, $"Direção {player.Facing} observada após giro prioritário; próxima tecla liberada", EventOrigin.Observed);
			return ActionResult.Ok("Direção observada alinhada; próxima tecla liberada");
		}
		if (direction == player.Facing)
		{
			return ActionResult.Ok("Já alinhado; próxima tecla liberada");
		}
		TurnResult turnResult = _orientation.RequestTurn(direction);
		if (!turnResult.Accepted)
		{
			return ActionResult.Rejected("Giro antes do combo recusado: " + turnResult.Reason);
		}
		_lastTurnAt = nowMs;
		_turnTargetId = target.Id;
		_log.Log(LogLevel.Info, $"Giro prioritário {direction} enviado antes da próxima tecla do combo; aguardando leitura nova", EventOrigin.Executed);
		return ActionResult.Deferred($"Giro {direction} enviado antes da magia; aguardando direção na memória");
	}
}
