using System;
using System.Collections.Generic;
using System.Linq;

namespace NinOnlineAutoAssist.Live;

public sealed record LiveBuffObservation(BuffPresence Presence, int? DurationSeconds, string Reason)
{
	public IReadOnlyList<ObservedBuff> ActiveSubs { get; init; } = Array.Empty<ObservedBuff>();

	public static LiveBuffObservation FromFrame(LiveFrame? frame, long nowMs)
	{
		bool flag = (object)frame == null || nowMs < frame.CapturedAtMs || nowMs - frame.CapturedAtMs > 500;
		if (!flag)
		{
			long readDurationMs = frame.ReadDurationMs;
			bool flag2 = ((readDurationMs < 0 || readDurationMs > 500) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			return new LiveBuffObservation(BuffPresence.Unknown, null, "Sem amostra recente — não significa buff inativo");
		}
		IReadOnlyList<ObservedBuff> readOnlyList = SubstitutionBuffs.Active(frame);
		if (readOnlyList.Count <= 0)
		{
			return new LiveBuffObservation(BuffPresence.Inactive, null, "Subs monitoradas ausentes na amostra recente");
		}
		return new LiveBuffObservation(BuffPresence.Active, frame.SubstitutionDuration, string.Join(" + ", readOnlyList.Select((ObservedBuff b) => b.Name)))
		{
			ActiveSubs = readOnlyList
		};
	}
}
