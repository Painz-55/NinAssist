namespace NinOnlineAutoAssist.Core.SubPunish;

public enum SubPunishState
{
    Idle = 0,
    WaitingForAttack = 1,
    SubActivationRequested = 2,
    SubActive = 3,
    SubTriggered = 4,
    RepositioningConfirmed = 5,
    TurningToAttacker = 6,
    CounterattackReady = 7,
    CounterattackExecuting = 8,
    Completed = 9,
    Cancelled = 10
}

public enum TargetSelectionPolicy
{
    /// <summary>Default. Abort the counter-attack unless the attacker identity is confirmed to match the original threat.</summary>
    CancelOnMismatch = 0,
    /// <summary>Adopt the confirmed attacker even if different from the originally detected threat.</summary>
    ProceedWithAttacker = 1
}

public sealed record SubPunishOptions
{
    public bool Enabled { get; init; } = false;
    /// <summary>Automatically request substitution when a valid threat is detected.</summary>
    public bool AutoPrepareSub { get; init; } = true;
    /// <summary>Explicit assisted mode: observe a manually activated buff, then counter the locked selected target.
    /// This does NOT identify the attacker or manufacture a threat event.</summary>
    public bool AdoptManualSubstitutionForSelectedTarget { get; init; } = false;
    /// <summary>Stop after observed alignment, without requesting an offensive action.</summary>
    public bool OrientationOnly { get; init; } = false;
    public TargetSelectionPolicy TargetSelectionPolicy { get; init; } = TargetSelectionPolicy.CancelOnMismatch;
    /// <summary>Max time (ms) allowed to align facing after teleport before cancelling.</summary>
    public long OrientationTimeoutMs { get; init; } = 1200;
    /// <summary>Max time (ms) to wait for the attack to land after a valid threat before giving up.</summary>
    public long ThreatExpiryMs { get; init; } = 3000;
}
