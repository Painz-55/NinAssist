namespace NinOnlineAutoAssist.Core.Config;

/// <summary>Small explicit keyboard vocabulary, shared by settings and native dispatch.</summary>
public static class LiveKeySettings
{
    public const string Disabled = "null";
    public static IReadOnlyDictionary<string, ushort> Keys { get; } = Build();
    public static string NormalizeGameKey(string key) => key.Length == 4 && key.StartsWith("Num", StringComparison.Ordinal) &&
        key[3] is >= '0' and <= '9' ? key[3].ToString() : key;
    private static Dictionary<string, ushort> Build()
    {
        var keys = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        for (char c = 'A'; c <= 'Z'; c++) keys[c.ToString()] = c;
        for (char c = '0'; c <= '9'; c++) keys[c.ToString()] = c;
        for (var i = 1; i <= 11; i++) keys[$"F{i}"] = (ushort)(0x6F + i);
        keys["RightCtrl"] = 0xA3; keys["LeftCtrl"] = 0xA2;
        keys["RightShift"] = 0xA1; keys["LeftShift"] = 0xA0;
        return keys;
    }

    public static string[] Validate(string[] combo, string[] activation)
    {
        if (combo is null || combo.Length != 3 || activation is null || activation.Length > 8)
            throw new ArgumentException("Escolha três posições para o combo e até oito teclas alternativas para Turn.");
        var all = combo.Concat(activation).ToArray();
        if (all.Any(k => string.IsNullOrWhiteSpace(k)) ||
            combo.Any(k => !k.Equals(Disabled, StringComparison.OrdinalIgnoreCase) && !Keys.ContainsKey(k)) ||
            activation.Any(k => !Keys.ContainsKey(k)))
            throw new ArgumentException("Tecla inválida. Use A–Z, 0–9, F1–F11, RightCtrl/LeftCtrl ou RightShift/LeftShift.");
        if (combo.Any(k => !k.Equals(Disabled, StringComparison.OrdinalIgnoreCase) && Keys[k] >= 0xA0) || combo.Contains("F", StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Combo não pode usar modificadores nem F (Substitution manual).");
        if (combo.Where(k => !k.Equals(Disabled, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
            combo.Count(k => !k.Equals(Disabled, StringComparison.OrdinalIgnoreCase)) ||
            activation.Distinct(StringComparer.OrdinalIgnoreCase).Count() != activation.Length)
            throw new ArgumentException("Não repita teclas dentro do combo ou dentro da lista de ativação do Turn.");
        return combo.Select(k => k.Equals(Disabled, StringComparison.OrdinalIgnoreCase) ? Disabled : Keys.Keys.Single(name => name.Equals(k, StringComparison.OrdinalIgnoreCase)))
            .Concat(activation.Select(k => Keys.Keys.Single(name => name.Equals(k, StringComparison.OrdinalIgnoreCase)))).ToArray();
    }
}
