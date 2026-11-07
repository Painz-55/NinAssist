using System.Text.Json.Serialization;

namespace NinOnlineAutoAssist.Core.Config;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AutoAttackMode { Melee, Fan, HisuyFan }

public sealed record CombatAssistSettings
{
    public bool UseAttack { get; init; } = true;
    public bool UseTool { get; init; } = true;
    public string AttackKey { get; init; } = "A";
    public string ToolKey { get; init; } = "S";
    public long AttackIntervalMs { get; init; } = 800;
    public long ToolIntervalMs { get; init; } = 1200;
    public double WeaponRadius { get; init; } = 1.0;
    public AutoAttackMode AttackMode { get; init; } = AutoAttackMode.Melee;
    // Initial attempt filters only; actual weapon hitboxes/ranges still need user calibration.
    public double FanRange { get; init; } = 6.0;
    public double FanHalfWidth { get; init; } = 0.60;
    // Null preserves the effective Tool-width behavior of configurations saved before this option existed.
    public double? FanProjectileHalfWidth { get; init; }
    [JsonIgnore]
    public double EffectiveFanHalfWidth => FanProjectileHalfWidth ?? ToolHalfWidth;
    public double HisuyFanRange { get; init; } = 3.0;
    public double HisuyFanHalfWidth { get; init; } = 1.0;
    public double ToolRange { get; init; } = 6.0;
    public double ToolHalfWidth { get; init; } = 0.30;
    public string FollowHoldKey { get; init; } = "RightCtrl";
    public double FollowStopDistance { get; init; } = 1.0;
    public long FollowStepIntervalMs { get; init; } = 120;
    public int StaminaDisableBelowPercent { get; init; } = 20;
    public int StaminaEnableAbovePercent { get; init; } = 50;

    public static void ValidateStaminaThresholds(int disableBelow, int enableAbove)
    {
        if (disableBelow < 0 || enableAbove > 100 || disableBelow >= enableAbove)
            throw new ArgumentException("Stamina: percentuais entre 0 e 100; desligar abaixo de deve ser menor que religar acima de.");
    }

    public void Validate()
    {
        if (!Enum.IsDefined(AttackMode)) throw new ArgumentException("Modo de AA inválido: Melee, Fan ou Hisuy Fan.");
        foreach (var range in new[] { FanRange, HisuyFanRange })
            if (!double.IsFinite(range) || range is <= 0 or > 30) throw new ArgumentException("Alcance de Fan/Hisuy Fan deve ser >0 e <=30 tiles.");
        foreach (var width in new[] { FanHalfWidth, HisuyFanHalfWidth })
            if (!double.IsFinite(width) || width is <= 0 or > 10) throw new ArgumentException("Meia-largura de Fan/Hisuy Fan deve ser >0 e <=10 tiles.");
        if (FanProjectileHalfWidth is { } fanWidth && (!double.IsFinite(fanWidth) || fanWidth is <= 0 or > 10))
            throw new ArgumentException("Meia-largura do projétil Fan deve ser >0 e <=10 tiles.");
        ValidateStaminaThresholds(StaminaDisableBelowPercent, StaminaEnableAbovePercent);
        foreach (var key in new[] { AttackKey, ToolKey, FollowHoldKey })
            if (string.IsNullOrWhiteSpace(key) || !LiveKeySettings.Keys.ContainsKey(key))
                throw new ArgumentException("Tecla de AA/Tool/Follow inválida; F12/setas são reservados.");
        if (LiveKeySettings.Keys[AttackKey] >= 0xA0 || LiveKeySettings.Keys[ToolKey] >= 0xA0 ||
            AttackKey.Equals("F", StringComparison.OrdinalIgnoreCase) || ToolKey.Equals("F", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("AA/Tool não podem usar modificadores nem F (Sub manual).");
        if (AttackKey.Equals(ToolKey, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("AA e Tool precisam de teclas distintas.");
        if (FollowHoldKey.Equals(AttackKey, StringComparison.OrdinalIgnoreCase) || FollowHoldKey.Equals(ToolKey, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A tecla segurada do Follow não pode ser uma tecla de ataque automático.");
        if (AttackIntervalMs is < 80 or > 60000 || ToolIntervalMs is < 80 or > 60000 || FollowStepIntervalMs is < 80 or > 2000)
            throw new ArgumentException("Intervalos: AA/Tool 80–60000 ms; Follow 80–2000 ms.");
        if (!double.IsFinite(WeaponRadius) || WeaponRadius is <= 0 or > 30 ||
            !double.IsFinite(ToolRange) || ToolRange is <= 0 or > 30 ||
            !double.IsFinite(ToolHalfWidth) || ToolHalfWidth is <= 0 or > 1 ||
            !double.IsFinite(FollowStopDistance) || FollowStopDistance is < 0.5 or > 30)
            throw new ArgumentException("Alcances devem ser finitos; raio/alcance até 30 tiles, meia-largura Tool até 1 tile, parada Follow >= 0,5 tile.");
    }
}
