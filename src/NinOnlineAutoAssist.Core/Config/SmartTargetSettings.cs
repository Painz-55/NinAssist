namespace NinOnlineAutoAssist.Core.Config;

/// <summary>Hold-to-select the nearest living player from another known village.</summary>
public sealed record SmartTargetSettings
{
    public double RadiusTiles { get; init; } = 12;
    public int MaxAttempts { get; init; } = 2;
    public int AttemptIntervalMs { get; init; } = 200;
    public string HoldKey { get; init; } = "Tab";

    public static IReadOnlyDictionary<string, ushort> HoldKeys { get; } = BuildKeys();

    private static Dictionary<string, ushort> BuildKeys()
    {
        var keys = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["Tab"] = 0x09, ["LeftShift"] = 0xA0, ["RightShift"] = 0xA1,
            ["LeftCtrl"] = 0xA2, ["RightCtrl"] = 0xA3,
            ["LeftAlt"] = 0xA4, ["RightAlt"] = 0xA5
        };
        for (int i = 1; i <= 11; i++) keys[$"F{i}"] = (ushort)(0x6F + i);
        return keys;
    }

    public void Validate()
    {
        if (!double.IsFinite(RadiusTiles) || RadiusTiles is < 1 or > 40 ||
            MaxAttempts is < 1 or > 10 || AttemptIntervalMs is < 150 or > 5000 ||
            HoldKey is null || !HoldKeys.ContainsKey(HoldKey))
            throw new ArgumentException("Smart Target: raio, tentativas, intervalo ou tecla inválidos.");
    }

    public AutoTargetSettings Selection() => new()
    {
        RadiusTiles = RadiusTiles,
        MaxAttempts = MaxAttempts,
        AttemptIntervalMs = AttemptIntervalMs,
        IgnoreSameVillage = true
    };
}
