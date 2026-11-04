using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;

namespace NinOnlineAutoAssist.Live;

public enum CombatAction { Attack, Tool }

/// <summary>Geometric attempt filter, NOT a claim about the server's exact hitbox or projectile collision.</summary>
public static class CombatGeometry
{
    public static bool InRange(LiveFrame frame, CombatAction action, CombatAssistSettings settings)
    {
        if (action is not (CombatAction.Attack or CombatAction.Tool)) return false;
        if (frame.Snapshot.Target is not { IsAlive: true, IsTargeted: true } target || !frame.Snapshot.Player.IsAlive) return false;
        var player = frame.Snapshot.Player;
        var dx = target.PositionX - player.PositionX;
        var dy = target.PositionY - player.PositionY;
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) return false;
        if (action == CombatAction.Attack && settings.AttackMode == AutoAttackMode.Melee)
            return dx * dx + dy * dy <= settings.WeaponRadius * settings.WeaponRadius;
        var (range, width) = action == CombatAction.Tool ? (settings.ToolRange, settings.ToolHalfWidth) : settings.AttackMode switch
        {
            AutoAttackMode.Fan => (settings.FanRange, settings.FanHalfWidth),
            AutoAttackMode.HisuyFan => (settings.HisuyFanRange, settings.HisuyFanHalfWidth),
            _ => (double.NaN, double.NaN)
        };
        var (forward, lateral) = player.Facing switch
        {
            Direction.Up => (-dy, Math.Abs(dx)), Direction.Down => (dy, Math.Abs(dx)),
            Direction.Left => (-dx, Math.Abs(dy)), Direction.Right => (dx, Math.Abs(dy)),
            _ => (double.NaN, double.NaN)
        };
        return forward > 0 && forward <= range && lateral <= width;
    }

    public static string DescribeAttack(CombatAssistSettings settings) => settings.AttackMode switch
    {
        AutoAttackMode.Melee => $"Melee radial <= {settings.WeaponRadius} tiles",
        AutoAttackMode.Fan => $"Fan frontal <= {settings.FanRange}, meia-largura {settings.FanHalfWidth} tiles",
        AutoAttackMode.HisuyFan => $"Hisuy Fan frontal <= {settings.HisuyFanRange}, meia-largura {settings.HisuyFanHalfWidth} tiles",
        _ => "AA inválido/bloqueado"
    };
}

public sealed class AutoAttackToolEngine
{
    private readonly IMonotonicClock _clock;
    private readonly Func<CombatAction, string, ActionResult> _execute;
    private readonly CombatAssistSettings _settings;
    private long? _lastAttack, _lastTool;
    private bool _preferTool;
    public string Status { get; private set; } = "AA/Tool OFF";

    public AutoAttackToolEngine(IMonotonicClock clock, CombatAssistSettings settings,
        Func<CombatAction, string, ActionResult> execute)
    { settings.Validate(); _clock = clock; _settings = settings; _execute = execute; }

    public void Reset() { _lastAttack = _lastTool = null; _preferTool = false; Status = "AA/Tool OFF"; }

    public void Update(LiveFrame? frame, bool allowed, bool higherPriorityBusy)
        => Update(frame, allowed, allowed, higherPriorityBusy);

    public void Update(LiveFrame? frame, bool attackAllowed, bool toolAllowed, bool higherPriorityBusy)
    {
        if ((!attackAllowed && !toolAllowed) || frame is null) { Status = "AA/Tool bloqueado/desligado"; return; }
        var now = _clock.NowMs;
        if (now < frame.CapturedAtMs || now - frame.CapturedAtMs > 500 || frame.ReadDurationMs is < 0 or > 500)
        { Status = "AA/Tool: dados antigos"; return; }
        if (higherPriorityBusy) { Status = "AA/Tool: aguardando SubPunish/giro"; return; }
        var attack = attackAllowed && _settings.UseAttack && (_lastAttack is null || now - _lastAttack >= _settings.AttackIntervalMs) &&
            CombatGeometry.InRange(frame, CombatAction.Attack, _settings);
        var tool = toolAllowed && _settings.UseTool && (_lastTool is null || now - _lastTool >= _settings.ToolIntervalMs) &&
            CombatGeometry.InRange(frame, CombatAction.Tool, _settings);
        if (!attack && !tool) { Status = "AA/Tool: aguardando intervalo/alcance/alinhamento frontal"; return; }
        var selected = tool && (!attack || _preferTool) ? CombatAction.Tool : CombatAction.Attack;
        // Timestamp every attempt, including refusal: never spin SendInput on a held/manual key or bind fault.
        if (selected == CombatAction.Attack) _lastAttack = now; else _lastTool = now;
        var result = _execute(selected, selected == CombatAction.Attack ? _settings.AttackKey : _settings.ToolKey);
        _preferTool = selected == CombatAction.Attack;
        var label = selected == CombatAction.Attack ? $"AA {_settings.AttackMode}" : "Tool";
        Status = result.Accepted ? $"{label}: tecla enviada; efeito/acerto NÃO confirmado" : $"{label}: {result.Reason}";
    }
}
