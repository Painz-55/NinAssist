namespace NinOnlineAutoAssist.Core.Config;

public sealed record AutoCastTurnSettings
{
    // Small pre-completion window to give the ordinary turn key time to reach the game's input poll.
    public int LeadTimeMs { get; init; } = 100;
    public void Validate()
    {
        if (LeadTimeMs is < 40 or > 250) throw new ArgumentException("Auto Cast Turn: antecedência deve ficar entre 40 e 250 ms.");
    }
}
