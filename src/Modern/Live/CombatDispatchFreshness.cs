namespace NinOnlineAutoAssist.Live;

public static class CombatDispatchFreshness
{
	public static bool Allows(LiveFrame? frame, long nowMs, int maximumAgeMs = 80)
	{
		if ((object)frame != null && nowMs >= frame.CapturedAtMs && nowMs - frame.CapturedAtMs <= maximumAgeMs && frame.ReadDurationMs >= 0)
		{
			return frame.ReadDurationMs <= maximumAgeMs;
		}
		return false;
	}
}
