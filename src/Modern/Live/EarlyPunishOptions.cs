namespace NinOnlineAutoAssist.Live;

public sealed record EarlyPunishOptions
{
	public long EarlyExpiryMarginMs { get; init; } = 1000L;

	public int AbsentSamplesRequired { get; init; } = 2;

	public long KeyIntervalMs { get; init; } = 120L;

	public string[] ComboKeys { get; init; } = new string[3] { "A", "Q", "S" };

	public long? FirstToSecondMs { get; init; }

	public long TriggerDelayMs { get; init; }

	public long? SecondToThirdMs { get; init; }

	public long SequenceTimeoutMs { get; init; } = 1500L;

	public string[]? EligibleSources { get; init; }
}
