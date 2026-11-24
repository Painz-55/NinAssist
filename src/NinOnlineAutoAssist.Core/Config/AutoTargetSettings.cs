namespace NinOnlineAutoAssist.Core.Config;

/// <summary>
/// Auto Target writes the selected player to the validated client's managed target state
/// and confirms the result on a subsequent read. Firebase, signed profile and ON gate apply.
/// </summary>
public sealed record AutoTargetSettings
{
    /// <summary>Maximum distance, in tiles, from the local player.</summary>
    public double RadiusTiles { get; init; } = 12;
    /// <summary>Upper bound of memory selection attempts per candidate before giving up.</summary>
    public int MaxAttempts { get; init; } = 4;
    /// <summary>Minimum delay between two attempts.</summary>
    public int AttemptIntervalMs { get; init; } = 400;
    /// <summary>Skip players that share the local player's known village.</summary>
    public bool IgnoreSameVillage { get; init; } = true;

    public void Validate()
    {
        if (!double.IsFinite(RadiusTiles) || RadiusTiles is < 1 or > 40)
            throw new ArgumentException("Auto Target: raio deve ficar entre 1 e 40 tiles.");
        if (MaxAttempts is < 1 or > 10)
            throw new ArgumentException("Auto Target: tentativas devem ficar entre 1 e 10.");
        if (AttemptIntervalMs is < 150 or > 5000)
            throw new ArgumentException("Auto Target: intervalo deve ficar entre 150 e 5000 ms.");
    }
}
