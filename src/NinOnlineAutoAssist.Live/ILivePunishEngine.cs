namespace NinOnlineAutoAssist.Live;

/// <summary>Common dispatch contract; each variant owns its independent evidence/state.</summary>
public interface ILivePunishEngine
{
    EarlyPunishState State { get; }
    string Status { get; }
    bool HasTimeForTurn { get; }
    bool BlocksAutoTurning { get; }
    long? NextKeyDueAtMs { get; }
    bool TriggerBuffsRemainAbsent(LiveFrame frame);
    bool HasUnprocessedRemoval(LiveFrame? frame);
    void Reset();
    void Update(LiveFrame? frame, bool enabled, bool authorized, bool safetyAllowed, string? blockReason = null);
}
