namespace NinOnlineAutoAssist.Core.Config;

/// <summary>Which player the final cast window should turn toward.</summary>
public enum CastTurnTargetMode
{
    /// <summary>Only the player selected in the game (the default, unchanged behaviour).</summary>
    Selected = 0,
    /// <summary>The closest living player inside the configured radius.</summary>
    Nearest = 1,
}

public sealed record AutoCastTurnSettings
{
    // Small pre-completion window to give the ordinary turn key time to reach the game's input poll.
    public int LeadTimeMs { get; init; } = 200;
    // Default: all valid observed casts. Only explicitly listed spell IDs are skipped.
    public int[] ExcludedSpellIds { get; init; } = [];
    // Chosen target for the final window; "Selected" preserves the previous behaviour.
    public CastTurnTargetMode TargetMode { get; init; } = CastTurnTargetMode.Selected;
    // Radius used only when TargetMode is Nearest.
    public double NearestRadiusTiles { get; init; } = 12;
    // Skip players sharing the local player's known village when TargetMode is Nearest.
    public bool NearestIgnoreSameVillage { get; init; } = true;

    public bool Allows(int spellId) => spellId > 0 && ExcludedSpellIds is not null && !ExcludedSpellIds.Contains(spellId);

    public void Validate()
    {
        if (LeadTimeMs is < 40 or > 250) throw new ArgumentException("Auto Cast Turn: antecedência deve ficar entre 40 e 250 ms.");
        if (ExcludedSpellIds is null || ExcludedSpellIds.Length > 128 ||
            ExcludedSpellIds.Any(id => id <= 0) || ExcludedSpellIds.Distinct().Count() != ExcludedSpellIds.Length)
            throw new ArgumentException("Auto Cast Turn: informe até 128 IDs positivos, sem repetição, para excluir.");
        if (!Enum.IsDefined(TargetMode))
            throw new ArgumentException("Auto Cast Turn: modo de alvo inválido.");
        if (!double.IsFinite(NearestRadiusTiles) || NearestRadiusTiles is < 1 or > 40)
            throw new ArgumentException("Auto Cast Turn: raio do alvo mais próximo deve ficar entre 1 e 40 tiles.");
    }
}
