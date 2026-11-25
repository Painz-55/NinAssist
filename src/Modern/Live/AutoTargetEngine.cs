using System;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Models;

namespace NinOnlineAutoAssist.Live;

/// <summary>
/// Requests selection of the closest living player and confirms it on a subsequent read.
/// Attempts are bounded; a request is never assumed to have succeeded.
/// </summary>
public sealed class AutoTargetEngine
{
	private readonly IMonotonicClock _clock;

	private readonly AutoTargetSettings _settings;

	private readonly Func<TargetInfo, ActionResult> _select;
	private readonly string _label;

	private long? _lastAttemptAt;

	private int _attempts;

	private string? _attemptTarget;

	public string Status { get; private set; } = "Auto Target OFF";

	public AutoTargetEngine(IMonotonicClock clock, AutoTargetSettings settings, Func<TargetInfo, ActionResult> select, string label = "Auto Target")
	{
		settings.Validate();
		_clock = clock;
		_settings = settings;
		_select = select;
		_label = label;
	}

	public void Reset()
	{
		_lastAttemptAt = null;
		_attempts = 0;
		_attemptTarget = null;
	}

	public void Update(LiveFrame? frame, bool enabled, bool allowed, bool higherPriorityBusy)
	{
		if (!enabled)
		{
			Reset();
			Status = _label + " OFF";
			return;
		}
		if (!allowed)
		{
			Reset();
			Status = _label + ": bloqueado pela checagem de segurança";
			return;
		}
		if (frame is null)
		{
			Status = _label + ": leitura indisponível";
			return;
		}
		TargetInfo? nearest = NearestTargetPolicy.Resolve(frame, _settings.RadiusTiles, _settings.IgnoreSameVillage, out string reason);
		if (nearest is null)
		{
			Reset();
			Status = $"{_label}: {reason} (raio {_settings.RadiusTiles:0.#} tiles)";
			return;
		}
		TargetInfo? selected = frame.Snapshot.Target;
		if (selected is { IsAlive: true, IsTargeted: true } && selected.Id == nearest.Id)
		{
			Reset();
			Status = $"{_label}: mais próximo selecionado ({nearest.Id})";
			return;
		}
		if (higherPriorityBusy)
		{
			Status = _label + ": aguardando prioridade do cast/combo";
			return;
		}
		if (!string.Equals(_attemptTarget, nearest.Id, StringComparison.Ordinal))
		{
			_attemptTarget = nearest.Id;
			_attempts = 0;
			_lastAttemptAt = null;
		}
		if (_attempts >= _settings.MaxAttempts)
		{
			Status = $"{_label}: {nearest.Id} não confirmado após {_attempts} tentativa(s); sem nova tentativa";
			return;
		}
		long nowMs = _clock.NowMs;
		if (_lastAttemptAt.HasValue && nowMs - _lastAttemptAt.Value < _settings.AttemptIntervalMs)
		{
			return;
		}
		if (_lastAttemptAt.HasValue && frame.CapturedAtMs <= _lastAttemptAt.Value)
		{
			Status = _label + ": aguardando leitura nova após a escrita";
			return;
		}
		_lastAttemptAt = nowMs;
		_attempts++;
		ActionResult result = _select(nearest);
		Status = (result.Accepted
			? $"{_label}: escrita {_attempts}/{_settings.MaxAttempts} em {nearest.Id}; aguardando confirmação pela leitura"
			: (_label + ": escrita recusada — " + result.Reason));
	}
}
