using System;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Models;

namespace NinOnlineAutoAssist.Live;

public sealed class AutoFollowEngine
{
	private readonly IMonotonicClock _clock;

	private readonly CombatAssistSettings _settings;

	private readonly Func<FollowStep, ActionResult> _move;

	private readonly Action _release;

	private bool _maintaining;

	private long? _stuckSince;

	private (double X, double Y)? _lastPosition;

	private string? _target;

	private bool _stuck;

	private FollowStep? _previousStep;

	public string Status { get; private set; } = "Follow OFF";

	public AutoFollowEngine(IMonotonicClock clock, CombatAssistSettings settings, Func<FollowStep, ActionResult> move, Action? release = null)
	{
		settings.Validate();
		_clock = clock;
		_settings = settings;
		_move = move;
		_release = release ?? ((Action)delegate
		{
		});
	}

	public void Reset()
	{
		_release();
		_stuckSince = null;
		_lastPosition = null;
		_target = null;
		_stuck = false;
		_previousStep = null;
		Status = "Follow: segure a tecla configurada";
	}

	public void Update(LiveFrame? frame, NavigationFrame? grid, bool allowed, bool higherPriorityBusy)
	{
		_maintaining = false;
		try
		{
			UpdateCore(frame, grid, allowed, higherPriorityBusy);
		}
		finally
		{
			if (!_maintaining)
			{
				_previousStep = null;
				_release();
			}
		}
	}

	private void UpdateCore(LiveFrame? frame, NavigationFrame? grid, bool allowed, bool higherPriorityBusy)
	{
		if (!allowed)
		{
			Reset();
			return;
		}
		long nowMs = _clock.NowMs;
		bool flag = (object)frame == null || (object)grid == null || nowMs < frame.CapturedAtMs || nowMs - frame.CapturedAtMs > 500;
		if (!flag)
		{
			long readDurationMs = frame.ReadDurationMs;
			bool flag2 = ((readDurationMs < 0 || readDurationMs > 500) ? true : false);
			flag = flag2;
		}
		if (flag || nowMs < grid.CapturedAtMs || nowMs - grid.CapturedAtMs > 500 || grid.Map != frame.Map || !grid.DynamicHazardsKnown)
		{
			Status = "Follow bloqueado: terreno/perigos ausentes, desconhecidos ou antigos";
			return;
		}
		TargetInfo target = frame.Snapshot.Target;
		if (!frame.Snapshot.Player.IsAlive || (object)target == null || !target.IsAlive || !target.IsTargeted)
		{
			Status = "Follow: sem alvo/jogador válido";
			return;
		}
		if (_target != target.Id)
		{
			Reset();
			_target = target.Id;
		}
		if (higherPriorityBusy)
		{
			_stuckSince = null;
			Status = "Follow: aguardando prioridade do SubPunish";
			return;
		}
		PlayerState player = frame.Snapshot.Player;
		if (Math.Sqrt(Math.Pow(target.PositionX - player.PositionX, 2.0) + Math.Pow(target.PositionY - player.PositionY, 2.0)) <= _settings.FollowStopDistance)
		{
			_stuckSince = null;
			Status = "Follow: distância de parada alcançada";
			return;
		}
		FollowStep followStep = FollowPathPlanner.Next(frame, grid, _settings.FollowStopDistance, _previousStep);
		if ((object)followStep == null)
		{
			_stuckSince = null;
			_lastPosition = null;
			Status = "Follow: sem caminho conhecido seguro; aguardando rota livre";
			return;
		}
		if (_stuck)
		{
			Status = "Follow parado: sem progresso; solte e pressione a tecla novamente";
			return;
		}
		(double, double)? lastPosition = _lastPosition;
		if (lastPosition.HasValue)
		{
			(double, double) valueOrDefault = lastPosition.GetValueOrDefault();
			if (Math.Abs(player.PositionX - valueOrDefault.Item1) + Math.Abs(player.PositionY - valueOrDefault.Item2) < 0.05)
			{
				long readDurationMs = _stuckSince.GetValueOrDefault();
				if (!_stuckSince.HasValue)
				{
					readDurationMs = nowMs;
					_stuckSince = readDurationMs;
				}
				long? stuckSince = _stuckSince;
				if (stuckSince.HasValue)
				{
					long valueOrDefault2 = stuckSince.GetValueOrDefault();
					if (nowMs - valueOrDefault2 > 1500)
					{
						_stuck = true;
						Status = "Follow parado: obstáculo/sem progresso";
						return;
					}
				}
				goto IL_02ba;
			}
		}
		_lastPosition = (player.PositionX, player.PositionY);
		_stuckSince = nowMs;
		goto IL_02ba;
		IL_02ba:
		ActionResult actionResult = _move(followStep);
		_maintaining = actionResult.Accepted;
		_previousStep = (actionResult.Accepted ? followStep : null);
		if (!actionResult.Accepted)
		{
			_stuckSince = null;
			_lastPosition = null;
		}
		Status = (actionResult.Accepted ? ("Follow: " + followStep.MovementText + "; evitando tiles/perigos observados") : ("Follow: " + actionResult.Reason));
	}
}
