namespace NinOnlineAutoAssist.Live;

public readonly record struct SafetyDecision(bool Allowed, string Reason);
public enum LiveInputKind { Turning, Punish, Attack, Tool, Follow, Stamina, CastTurning }

public static class LiveSafetyGate
{
    public static SafetyDecision CheckCastTurning(LiveFrame? frame, long nowMs, bool enabled, bool foreground, bool emergency, bool textEntryFocused)
    {
        if (!enabled) return new(false, "Auto Cast Turn OFF");
        var common = CheckTargetedModule(frame, nowMs, true, true, foreground, emergency, textEntryFocused);
        if (!common.Allowed) return common;
        if (!AutoCastTurnEngine.Fresh(frame, nowMs) || frame!.Cast is not { Valid: true })
            return new(false, "Auto Cast Turn: cast desconhecido ou dados antigos");
        return new(true, "Auto Cast Turn autorizado: alvo + foco + leitura de cast válida");
    }

    // manualMovement remains in public signatures for compatibility. Live automation intentionally ignores it.
    public static SafetyDecision Check(LiveFrame? frame, long nowMs, bool foreground, bool holdPressed,
        bool emergency, bool manualMovement, bool textEntryFocused)
        => CheckCommon(frame, nowMs, foreground, holdPressed, true, emergency, textEntryFocused);

    public static SafetyDecision CheckTurning(LiveFrame? frame, long nowMs, bool enabled, bool foreground,
        bool holdPressed, bool emergency, bool manualMovement, bool textEntryFocused)
        => !enabled ? new(false, "Auto Turning OFF")
            : CheckCommon(frame, nowMs, foreground, holdPressed, true, emergency, textEntryFocused);

    // Combo has no target/hold-key dependency. Automatic Sub turning has its own additional window/target gate.
    public static SafetyDecision CheckPunish(LiveFrame? frame, long nowMs, bool enabled, bool authorized,
        bool foreground, bool emergency, bool manualMovement, bool textEntryFocused)
        => !enabled ? new(false, "SubPunish OFF")
            : !authorized ? new(false, "Combo desautorizado")
            : CheckCommon(frame, nowMs, foreground, false, false, emergency, textEntryFocused);

    public static SafetyDecision CheckSubstitutionTurning(LiveFrame? frame, long nowMs, bool enabled, bool authorized,
        bool windowOpen, bool foreground, bool emergency, bool manualMovement, bool textEntryFocused)
    {
        var common = CheckPunish(frame, nowMs, enabled, authorized, foreground, emergency, manualMovement, textEntryFocused);
        if (!common.Allowed) return common;
        if (!windowOpen) return new(false, "Janela de giro da Sub fechada");
        if (frame!.Snapshot.Target is not { IsAlive: true, IsTargeted: true })
            return new(false, "No live selected player target");
        return new(true, "ARMED: giro automático da Sub — buff ativo ou até 1s após sumir");
    }

    public static SafetyDecision CheckTargetedModule(LiveFrame? frame, long nowMs, bool enabled, bool authorized,
        bool foreground, bool emergency, bool textEntryFocused)
    {
        if (!enabled) return new(false, "Módulo OFF");
        if (!authorized) return new(false, "AA/Tool desautorizado");
        var common = CheckCommon(frame, nowMs, foreground, false, false, emergency, textEntryFocused);
        if (!common.Allowed) return common;
        return frame!.Snapshot.Target is { IsAlive: true, IsTargeted: true }
            ? new(true, "ARMED: alvo vivo/selecionado + foco + dados válidos") : new(false, "Sem alvo vivo/selecionado");
    }

    public static SafetyDecision CheckStamina(LiveFrame? frame, long nowMs, bool enabled, bool foreground,
        bool emergency, bool textEntryFocused)
    {
        if (!enabled) return new(false, "Auto Stamina OFF");
        var common = CheckCommon(frame, nowMs, foreground, false, false, emergency, textEntryFocused);
        if (!common.Allowed) return common;
        return frame!.Stamina is { Valid: true } ? new(true, "Auto Stamina autorizado pelo módulo ON")
            : new(false, "Stamina/corrida desconhecida");
    }

    private static SafetyDecision CheckCommon(LiveFrame? frame, long nowMs, bool foreground, bool holdPressed,
        bool requireHold, bool emergency, bool textEntryFocused)
    {
        if (emergency) return new(false, "Emergency stop");
        if (!foreground) return new(false, "Game is not foreground");
        if (requireHold && !holdPressed) return new(false, "Segure uma tecla configurada para Turn");
        if (textEntryFocused) return new(false, "Text entry has focus");
        if (frame is null || nowMs < frame.CapturedAtMs || nowMs - frame.CapturedAtMs > 500)
            return new(false, "Missing/stale state");
        if (frame.ReadDurationMs is < 0 or > 500) return new(false, "Read duration invalid/too slow");
        if (!frame.Snapshot.Player.IsAlive) return new(false, "Player is dead");
        if (requireHold && frame.Snapshot.Target is not { IsAlive: true, IsTargeted: true }) return new(false, "No live selected player target");
        return new(true, requireHold ? "ARMED: observed state + foreground + activation key"
            : "ARMED: Punish autorizado, sem tecla de ativação");
    }
}
