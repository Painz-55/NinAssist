using System.Text.Json;

namespace NinOnlineAutoAssist.Core.Config;

public static class RunBindingValidator
{
    public static ushort Resolve(IReadOnlyDictionary<string, string[]> bindings, ushort followKey)
    {
        var keys = new Dictionary<string, ushort> { ["LControl"] = 0xA2, ["RControl"] = 0xA3,
            ["LShift"] = 0xA0, ["RShift"] = 0xA1 };
        if (!bindings.TryGetValue("Run", out var run) || run.Length != 1 || !keys.TryGetValue(run[0], out var key))
            throw new ArgumentException("Run precisa ter um único atalho Ctrl/Shift sem modificadores.");
        if (key == followKey) throw new ArgumentException("Run e ativação do Follow não podem usar a mesma tecla.");
        var modifier = key is 0xA2 or 0xA3 ? "ControlModifier" : "ShiftModifier";
        var conflicts = bindings.Where(b => b.Key != "Run" && b.Key != modifier && b.Value.Contains(run[0]))
            .Select(b => b.Key).ToArray();
        if (conflicts.Length > 0) throw new ArgumentException($"Run compartilha tecla com ação: {string.Join(", ", conflicts)}.");
        return key;
    }

    public static bool IsToggleMode(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var value = doc.RootElement.GetProperty("Options").GetProperty("HoldToRun");
        return value.ValueKind == JsonValueKind.False ||
            (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var hold) && !hold);
    }
}
