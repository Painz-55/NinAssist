using NinOnlineAutoAssist.Core.Models;

namespace NinOnlineAutoAssist.Live;

public static class LiveSafetyGate
{
	public static SafetyDecision CheckCastTurning(LiveFrame? frame, long nowMs, bool enabled, bool foreground, bool emergency, bool textEntryFocused, bool requiresSelectedTarget = true)
	{
		if (!enabled)
		{
			return new SafetyDecision(Allowed: false, "Auto Cast Turn OFF");
		}
		SafetyDecision result = (requiresSelectedTarget
			? CheckTargetedModule(frame, nowMs, enabled: true, authorized: true, foreground, emergency, textEntryFocused)
			: CheckCommon(frame, nowMs, foreground, holdPressed: false, requireHold: false, emergency, textEntryFocused));
		if (!result.Allowed)
		{
			return result;
		}
		if (!requiresSelectedTarget && frame.MapPlayers is null)
		{
			return new SafetyDecision(Allowed: false, "Auto Cast Turn: lista de jogadores indisponível para o alvo mais próximo");
		}
		if (AutoCastTurnEngine.Fresh(frame, nowMs))
		{
			CastObservation cast = frame.Cast;
			if ((object)cast != null && cast.Valid)
			{
				return new SafetyDecision(Allowed: true, "Auto Cast Turn autorizado: alvo + foco + leitura de cast válida");
			}
		}
		return new SafetyDecision(Allowed: false, "Auto Cast Turn: cast desconhecido ou dados antigos");
	}

	public static SafetyDecision CheckAutoTarget(LiveFrame? frame, long nowMs, bool enabled, bool foreground, bool emergency, bool textEntryFocused)
	{
		if (!enabled)
		{
			return new SafetyDecision(Allowed: false, "Auto Target OFF");
		}
		SafetyDecision result = CheckCommon(frame, nowMs, foreground, holdPressed: false, requireHold: false, emergency, textEntryFocused);
		if (!result.Allowed)
		{
			return result;
		}
		if (frame.MapPlayers is null)
		{
			return new SafetyDecision(Allowed: false, "Auto Target: lista de jogadores indisponível");
		}
		return new SafetyDecision(Allowed: true, "Auto Target autorizado: lista de jogadores + foco + dados válidos");
	}

	public static SafetyDecision Check(LiveFrame? frame, long nowMs, bool foreground, bool holdPressed, bool emergency, bool manualMovement, bool textEntryFocused)
	{
		return CheckCommon(frame, nowMs, foreground, holdPressed, requireHold: true, emergency, textEntryFocused);
	}

	public static SafetyDecision CheckTurning(LiveFrame? frame, long nowMs, bool enabled, bool foreground, bool holdPressed, bool emergency, bool manualMovement, bool textEntryFocused)
	{
		if (enabled)
		{
			return CheckCommon(frame, nowMs, foreground, holdPressed, requireHold: true, emergency, textEntryFocused);
		}
		return new SafetyDecision(Allowed: false, "Auto Turning OFF");
	}

	public static SafetyDecision CheckPunish(LiveFrame? frame, long nowMs, bool enabled, bool authorized, bool foreground, bool emergency, bool manualMovement, bool textEntryFocused)
	{
		if (enabled)
		{
			if (authorized)
			{
				return CheckCommon(frame, nowMs, foreground, holdPressed: false, requireHold: false, emergency, textEntryFocused);
			}
			return new SafetyDecision(Allowed: false, "Combo desautorizado");
		}
		return new SafetyDecision(Allowed: false, "SubPunish OFF");
	}

	public static SafetyDecision CheckSubstitutionTurning(LiveFrame? frame, long nowMs, bool enabled, bool authorized, bool windowOpen, bool foreground, bool emergency, bool manualMovement, bool textEntryFocused)
	{
		SafetyDecision result = CheckPunish(frame, nowMs, enabled, authorized, foreground, emergency, manualMovement, textEntryFocused);
		if (!result.Allowed)
		{
			return result;
		}
		if (!windowOpen)
		{
			return new SafetyDecision(Allowed: false, "Janela de giro da Sub fechada");
		}
		TargetInfo target = frame.Snapshot.Target;
		if ((object)target == null || !target.IsAlive || !target.IsTargeted)
		{
			return new SafetyDecision(Allowed: false, "No live selected player target");
		}
		return new SafetyDecision(Allowed: true, "ARMED: giro automático da Sub — buff ativo ou até 1s após sumir");
	}

	public static SafetyDecision CheckTargetedModule(LiveFrame? frame, long nowMs, bool enabled, bool authorized, bool foreground, bool emergency, bool textEntryFocused)
	{
		if (!enabled)
		{
			return new SafetyDecision(Allowed: false, "Módulo OFF");
		}
		if (!authorized)
		{
			return new SafetyDecision(Allowed: false, "AA/Tool desautorizado");
		}
		SafetyDecision result = CheckCommon(frame, nowMs, foreground, holdPressed: false, requireHold: false, emergency, textEntryFocused);
		if (!result.Allowed)
		{
			return result;
		}
		TargetInfo target = frame.Snapshot.Target;
		if ((object)target == null || !target.IsAlive || !target.IsTargeted)
		{
			return new SafetyDecision(Allowed: false, "Sem alvo vivo/selecionado");
		}
		return new SafetyDecision(Allowed: true, "ARMED: alvo vivo/selecionado + foco + dados válidos");
	}

	public static SafetyDecision CheckStamina(LiveFrame? frame, long nowMs, bool enabled, bool foreground, bool emergency, bool textEntryFocused)
	{
		if (!enabled)
		{
			return new SafetyDecision(Allowed: false, "Auto Stamina OFF");
		}
		SafetyDecision result = CheckCommon(frame, nowMs, foreground, holdPressed: false, requireHold: false, emergency, textEntryFocused);
		if (!result.Allowed)
		{
			return result;
		}
		StaminaObservation stamina = frame.Stamina;
		if ((object)stamina == null || !stamina.Valid)
		{
			return new SafetyDecision(Allowed: false, "Stamina/corrida desconhecida");
		}
		return new SafetyDecision(Allowed: true, "Auto Stamina autorizado pelo módulo ON");
	}

	private static SafetyDecision CheckCommon(LiveFrame? frame, long nowMs, bool foreground, bool holdPressed, bool requireHold, bool emergency, bool textEntryFocused)
	{
		if (emergency)
		{
			return new SafetyDecision(Allowed: false, "Emergency stop");
		}
		if (!foreground)
		{
			return new SafetyDecision(Allowed: false, "Game is not foreground");
		}
		if (requireHold && !holdPressed)
		{
			return new SafetyDecision(Allowed: false, "Segure uma tecla configurada para Turn");
		}
		if (textEntryFocused)
		{
			return new SafetyDecision(Allowed: false, "Text entry has focus");
		}
		if ((object)frame == null || nowMs < frame.CapturedAtMs || nowMs - frame.CapturedAtMs > 500)
		{
			return new SafetyDecision(Allowed: false, "Missing/stale state");
		}
		long readDurationMs = frame.ReadDurationMs;
		if ((readDurationMs < 0 || readDurationMs > 500) ? true : false)
		{
			return new SafetyDecision(Allowed: false, "Read duration invalid/too slow");
		}
		if (!frame.Snapshot.Player.IsAlive)
		{
			return new SafetyDecision(Allowed: false, "Player is dead");
		}
		if (requireHold)
		{
			TargetInfo target = frame.Snapshot.Target;
			if ((object)target == null || !target.IsAlive || !target.IsTargeted)
			{
				return new SafetyDecision(Allowed: false, "No live selected player target");
			}
		}
		return new SafetyDecision(Allowed: true, requireHold ? "ARMED: observed state + foreground + activation key" : "ARMED: Punish autorizado, sem tecla de ativação");
	}
}
