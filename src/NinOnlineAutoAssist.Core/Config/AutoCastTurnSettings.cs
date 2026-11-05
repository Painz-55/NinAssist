namespace NinOnlineAutoAssist.Core.Config;

public sealed record AutoCastTurnSettings
{
    // Small pre-completion window to give the ordinary turn key time to reach the game's input poll.
    public int LeadTimeMs { get; init; } = 100;
    // Default: all valid observed casts. Only explicitly listed spell IDs are skipped.
    public int[] ExcludedSpellIds { get; init; } = [];
    public bool Allows(int spellId) => spellId > 0 && ExcludedSpellIds is not null && !ExcludedSpellIds.Contains(spellId);
    public void Validate()
    {
        if (LeadTimeMs is < 40 or > 250) throw new ArgumentException("Auto Cast Turn: antecedência deve ficar entre 40 e 250 ms.");
        if (ExcludedSpellIds is null || ExcludedSpellIds.Length > 128 ||
            ExcludedSpellIds.Any(id => id <= 0) || ExcludedSpellIds.Distinct().Count() != ExcludedSpellIds.Length)
            throw new ArgumentException("Auto Cast Turn: informe até 128 IDs positivos, sem repetição, para excluir.");
    }
}
