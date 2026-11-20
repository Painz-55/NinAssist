using System.Collections.Generic;

namespace NinOnlineAutoAssist.Live;

public interface ILivePunishEngine
{
	EarlyPunishState State { get; }

	string Status { get; }

	bool HasTimeForTurn { get; }

	bool BlocksAutoTurning { get; }

	long? NextKeyDueAtMs { get; }

	IReadOnlyList<string> TriggerSources { get; }

	bool TriggerBuffsRemainAbsent(LiveFrame frame);

	bool HasUnprocessedRemoval(LiveFrame? frame);

	void Reset();

	void Update(LiveFrame? frame, bool enabled, bool authorized, bool safetyAllowed, string? blockReason = null);
}
