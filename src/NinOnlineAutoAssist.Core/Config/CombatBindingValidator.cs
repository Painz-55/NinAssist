namespace NinOnlineAutoAssist.Core.Config;

/// <summary>Shuriken/tools are hotbar items, NEVER the separate weapon-throw action.</summary>
public static class CombatBindingValidator
{
    public static bool IsHotbarSlot(string action)
    {
        if (action.StartsWith("HotbarF", StringComparison.Ordinal))
            return int.TryParse(action.AsSpan(7), out var number) && number is >= 1 and <= 10 && action == $"HotbarF{number}";
        return action.Length == 7 && action.StartsWith("Hotbar", StringComparison.Ordinal) && action[6] is >= '0' and <= '9';
    }

    public static string ResolveToolSlot(IReadOnlyDictionary<string, string[]> bindings, string key)
    {
        if (bindings.TryGetValue("ThrowWeapon", out var throwing) && throwing.Any(k =>
            LiveKeySettings.NormalizeGameKey(k).Equals(key, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A tecla de Tool também aciona ThrowWeapon (arremesso da arma). Use uma tecla de hotbar sem esse conflito.");
        var slots = bindings.Where(pair => IsHotbarSlot(pair.Key) && pair.Value.Any(k =>
                LiveKeySettings.NormalizeGameKey(k).Equals(key, StringComparison.OrdinalIgnoreCase)))
            .Select(pair => pair.Key).ToArray();
        if (slots.Length == 0)
            throw new ArgumentException("A Tool precisa usar a tecla de um slot da hotbar onde você colocou a shuriken/kunai. Exemplo: S em HotbarF7. Não use ThrowWeapon.");
        if (slots.Length > 1)
            throw new ArgumentException("A tecla de Tool aciona vários slots da hotbar. Use uma tecla de slot única para não ativar outros itens/jutsus.");
        return slots[0];
    }
}
