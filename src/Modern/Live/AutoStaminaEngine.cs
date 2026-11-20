using System;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;

namespace NinOnlineAutoAssist.Live;

public sealed class AutoStaminaEngine
{
	private readonly IMonotonicClock _clock;

	private readonly Func<bool, ActionResult> _toggle;

	private readonly int _disableBelow;

	private readonly int _enableAbove;

	private string? _player;

	private int _map;

	private long? _sentAt;

	private long? _sentCapture;

	private bool _faulted;

	private bool _sentDesired;

	public bool? DesiredRunning { get; private set; }

	public string Status { get; private set; } = "Auto Stamina OFF";

	public AutoStaminaEngine(IMonotonicClock clock, Func<bool, ActionResult> toggle, int disableBelowPercent = 20, int enableAbovePercent = 50)
	{
		CombatAssistSettings.ValidateStaminaThresholds(disableBelowPercent, enableAbovePercent);
		_clock = clock;
		_toggle = toggle;
		_disableBelow = disableBelowPercent;
		_enableAbove = enableAbovePercent;
	}

	public void Reset()
	{
		DesiredRunning = null;
		_sentAt = (_sentCapture = null);
		_player = null;
		_map = 0;
		_faulted = false;
		Status = "Auto Stamina OFF";
	}

	public bool NeedsToggle(LiveFrame frame)
	{
		if (!_faulted)
		{
			long? sentAt = _sentAt;
			if (!sentAt.HasValue)
			{
				bool? desiredRunning = DesiredRunning;
				if (desiredRunning.HasValue)
				{
					bool valueOrDefault = desiredRunning == true;
					StaminaObservation stamina = frame.Stamina;
					if ((object)stamina != null && stamina.Valid && stamina.Running != valueOrDefault)
					{
						if (!valueOrDefault)
						{
							return stamina.Percent <= (double)_enableAbove;
						}
						return stamina.Percent >= (double)_disableBelow;
					}
				}
			}
		}
		return false;
	}

	public void Update(LiveFrame? frame, bool enabled, bool allowed, bool higherPriorityBusy)
	{
		if (!enabled)
		{
			Reset();
			return;
		}
		StaminaObservation staminaObservation = frame?.Stamina;
		bool flag = (object)staminaObservation == null || !staminaObservation.Valid || _clock.NowMs < frame.CapturedAtMs || _clock.NowMs - frame.CapturedAtMs > 500;
		if (!flag)
		{
			long readDurationMs = frame.ReadDurationMs;
			bool flag2 = ((readDurationMs < 0 || readDurationMs > 500) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			Status = "Stamina: dados ausentes/antigos/inválidos";
			return;
		}
		if (_player != frame.Snapshot.Player.Id || _map != frame.Map)
		{
			Reset();
			_player = frame.Snapshot.Player.Id;
			_map = frame.Map;
		}
		if (staminaObservation.Percent < (double)_disableBelow)
		{
			DesiredRunning = false;
		}
		else if (staminaObservation.Percent > (double)_enableAbove)
		{
			DesiredRunning = true;
		}
		else
		{
			bool? desiredRunning = DesiredRunning;
			flag = desiredRunning == true;
			if (!desiredRunning.HasValue)
			{
				flag = staminaObservation.Running;
				bool? desiredRunning2 = flag;
				DesiredRunning = desiredRunning2;
			}
		}
		string text = $"SP {staminaObservation.Current}/{staminaObservation.Maximum} ({staminaObservation.Percent:0}%) · corrida {(staminaObservation.Running ? "ON" : "OFF")}";
		long? sentAt = _sentAt;
		if (sentAt.HasValue)
		{
			long valueOrDefault = sentAt.GetValueOrDefault();
			if (frame.CapturedAtMs > _sentCapture && staminaObservation.Running == _sentDesired)
			{
				_sentAt = (_sentCapture = null);
			}
			else
			{
				if (_clock.NowMs - valueOrDefault < 1500)
				{
					Status = text + " · aguardando confirmação";
					return;
				}
				_faulted = true;
				_sentAt = (_sentCapture = null);
			}
		}
		if (_faulted)
		{
			Status = text + " · sem confirmação; desligue/ligue o módulo para rearmar";
			return;
		}
		if (!allowed || higherPriorityBusy)
		{
			Status = text + " · bloqueado/aguardando prioridade";
			return;
		}
		if (!NeedsToggle(frame))
		{
			Status = text;
			return;
		}
		ActionResult actionResult = _toggle(DesiredRunning.Value);
		if (!actionResult.Accepted)
		{
			_faulted = true;
			Status = text + " · " + actionResult.Reason + "; rearme o módulo";
			return;
		}
		_sentAt = _clock.NowMs;
		_sentCapture = frame.CapturedAtMs;
		_sentDesired = DesiredRunning.Value;
		Status = text + " · solicitado " + (_sentDesired ? "ON" : "OFF") + "; aguardando confirmação";
	}
}
