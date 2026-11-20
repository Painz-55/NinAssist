using System;
using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;

namespace NinOnlineAutoAssist.Live;

public sealed class AutoAttackToolEngine
{
	private readonly IMonotonicClock _clock;

	private readonly Func<CombatAction, string, ActionResult> _execute;

	private readonly Func<LiveFrame, Direction, ActionResult>? _prepareAttack;

	private readonly CombatAssistSettings _settings;

	private long? _lastAttack;

	private long? _lastTool;

	private bool _preferTool;

	public string Status { get; private set; } = "AA/Tool OFF";

	public AutoAttackToolEngine(IMonotonicClock clock, CombatAssistSettings settings, Func<CombatAction, string, ActionResult> execute, Func<LiveFrame, Direction, ActionResult>? prepareAttack = null)
	{
		settings.Validate();
		_clock = clock;
		_settings = settings;
		_execute = execute;
		_prepareAttack = prepareAttack;
	}

	public void Reset()
	{
		_lastAttack = (_lastTool = null);
		_preferTool = false;
		Status = "AA/Tool OFF";
	}

	public void Update(LiveFrame? frame, bool allowed, bool higherPriorityBusy)
	{
		Update(frame, allowed, allowed, higherPriorityBusy);
	}

	public void Update(LiveFrame? frame, bool attackAllowed, bool toolAllowed, bool higherPriorityBusy)
	{
		if ((!attackAllowed && !toolAllowed) || (object)frame == null)
		{
			Status = "AA/Tool bloqueado/desligado";
			return;
		}
		long nowMs = _clock.NowMs;
		bool flag = nowMs < frame.CapturedAtMs || nowMs - frame.CapturedAtMs > 500;
		if (!flag)
		{
			long readDurationMs = frame.ReadDurationMs;
			bool flag2 = ((readDurationMs < 0 || readDurationMs > 500) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			Status = "AA/Tool: dados antigos";
			return;
		}
		if (higherPriorityBusy)
		{
			Status = "AA/Tool: aguardando SubPunish/giro";
			return;
		}
		bool flag3 = CombatDispatchFreshness.Allows(frame, nowMs);
		Direction direction = ((_settings.AttackMode != AutoAttackMode.Melee) ? CombatGeometry.AttackDirection(frame, _settings) : Direction.None);
		int num;
		if (attackAllowed && _settings.UseAttack)
		{
			long? lastAttack = _lastAttack;
			if (!lastAttack.HasValue || nowMs - _lastAttack >= _settings.AttackIntervalMs)
			{
				num = (((_settings.AttackMode == AutoAttackMode.Melee) ? CombatGeometry.InRange(frame, CombatAction.Attack, _settings) : (flag3 && direction != Direction.None && _prepareAttack != null)) ? 1 : 0);
				goto IL_015f;
			}
		}
		num = 0;
		goto IL_015f;
		IL_015f:
		bool flag4 = (byte)num != 0;
		int num2;
		if (toolAllowed && _settings.UseTool)
		{
			long? lastAttack = _lastTool;
			num2 = ((!lastAttack.HasValue || nowMs - _lastTool >= _settings.ToolIntervalMs) ? 1 : 0);
		}
		else
		{
			num2 = 0;
		}
		bool flag5 = ((uint)num2 & (flag3 ? 1u : 0u)) != 0 && CombatGeometry.InRange(frame, CombatAction.Tool, _settings);
		if (!flag4 && !flag5)
		{
			Status = (flag3 ? "AA/Tool: aguardando intervalo/alcance/alinhamento frontal" : "AA/Tool: aguardando leitura rápida de direção (sem gastar intervalo)");
			return;
		}
		CombatAction combatAction = ((flag5 && (!flag4 || _preferTool)) ? CombatAction.Tool : CombatAction.Attack);
		flag = combatAction == CombatAction.Attack;
		if (flag)
		{
			AutoAttackMode attackMode = _settings.AttackMode;
			bool flag2 = (uint)(attackMode - 1) <= 1u;
			flag = flag2;
		}
		if (flag)
		{
			ActionResult actionResult = _prepareAttack(frame, direction);
			if (!actionResult.Accepted)
			{
				Status = $"AA {_settings.AttackMode}: {actionResult.Reason}";
				return;
			}
		}
		if (combatAction == CombatAction.Attack)
		{
			_lastAttack = nowMs;
		}
		else
		{
			_lastTool = nowMs;
		}
		ActionResult actionResult2 = _execute(combatAction, (combatAction == CombatAction.Attack) ? _settings.AttackKey : _settings.ToolKey);
		_preferTool = combatAction == CombatAction.Attack;
		string text = ((combatAction == CombatAction.Attack) ? $"AA {_settings.AttackMode}" : "Tool");
		Status = (actionResult2.Accepted ? (text + ": tecla enviada; efeito/acerto NÃO confirmado") : (text + ": " + actionResult2.Reason));
	}
}
