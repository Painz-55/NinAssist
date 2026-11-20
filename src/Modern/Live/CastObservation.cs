using System;

namespace NinOnlineAutoAssist.Live;

public sealed record CastObservation(bool Active, int SpellId, long StartedAtGameMs, long TotalMs, long RemainingMs)
{
	public long? ObservedAtMs { get; init; }

	public bool Valid
	{
		get
		{
			if (Active)
			{
				if (SpellId > 0 && StartedAtGameMs > 0)
				{
					long totalMs = TotalMs;
					if (totalMs >= 100 && totalMs <= 60000 && RemainingMs >= 0)
					{
						return RemainingMs <= TotalMs;
					}
				}
				return false;
			}
			return true;
		}
	}

	public static CastObservation? Decode(int uiSpell, int uiStart, int actorSpell, int actorEnd, long gameNow)
	{
		if (gameNow <= 0)
		{
			return null;
		}
		if (uiSpell == 0 && uiStart == 0 && actorSpell == 0 && actorEnd == 0)
		{
			return new CastObservation(Active: false, 0, 0L, 0L, 0L);
		}
		if (uiSpell <= 0 || actorSpell != uiSpell || uiStart <= 0 || actorEnd <= uiStart || gameNow < uiStart)
		{
			return null;
		}
		CastObservation castObservation = new CastObservation(Active: true, uiSpell, uiStart, (long)actorEnd - (long)uiStart, Math.Max(0L, actorEnd - gameNow));
		if (!castObservation.Valid)
		{
			return null;
		}
		return castObservation;
	}
}
