using System;
using System.Collections.Generic;
using System.Linq;

namespace NinOnlineAutoAssist.Live;

internal sealed class SubstitutionRemovalTracker
{
	private sealed class Cycle
	{
		public long? Expiry;

		public bool WasActive;

		public bool Consumed;

		public int Absent;
	}

	private readonly Dictionary<string, Cycle> _cycles = new Dictionary<string, Cycle>(StringComparer.Ordinal);

	public void Reset()
	{
		_cycles.Clear();
	}

	public RemovalSample Update(LiveFrame frame, long nowMs, long marginMs, int samplesRequired, Func<string, bool>? eligible = null)
	{
		Dictionary<string, ObservedBuff> dictionary = (from b in SubstitutionBuffs.Active(frame)
			where eligible?.Invoke(b.Name) ?? true
			group b by b.Name).ToDictionary((IGrouping<string, ObservedBuff> g) => g.Key, (IGrouping<string, ObservedBuff> g) => new ObservedBuff(g.Key, g.Any((ObservedBuff b) => !b.RemainingSeconds.HasValue) ? ((int?)null) : g.Min((ObservedBuff b) => b.RemainingSeconds)));
		foreach (string key in dictionary.Keys)
		{
			_cycles.TryAdd(key, new Cycle());
		}
		List<EarlyRemoval> list = new List<EarlyRemoval>();
		int num = 0;
		bool knownDuration = false;
		bool expired = false;
		bool newCycle = false;
		foreach (var (text2, cycle2) in _cycles)
		{
			if (dictionary.TryGetValue(text2, out var value))
			{
				if (!cycle2.WasActive && (cycle2.Absent == 0 || cycle2.Consumed))
				{
					cycle2.Expiry = null;
					cycle2.Consumed = false;
					newCycle = true;
				}
				cycle2.WasActive = true;
				cycle2.Absent = 0;
				switch (value.RemainingSeconds)
				{
				case 0:
				case 1:
				case 2:
				case 3:
				case 4:
				case 5:
				case 6:
				case 7:
				case 8:
				case 9:
				case 10:
				case 11:
				case 12:
				case 13:
				case 14:
				case 15:
				case 16:
				case 17:
				case 18:
				case 19:
				case 20:
				case 21:
				case 22:
				case 23:
				case 24:
				case 25:
				case 26:
				case 27:
				case 28:
				case 29:
				case 30:
				case 31:
				case 32:
				case 33:
				case 34:
				case 35:
				case 36:
				case 37:
				case 38:
				case 39:
				case 40:
				case 41:
				case 42:
				case 43:
				case 44:
				case 45:
				case 46:
				case 47:
				case 48:
				case 49:
				case 50:
				case 51:
				case 52:
				case 53:
				case 54:
				case 55:
				case 56:
				case 57:
				case 58:
				case 59:
				case 60:
				{
					long num2 = frame.CapturedAtMs + (long)value.RemainingSeconds.Value * 1000L;
					long? expiry = cycle2.Expiry;
					long value2;
					if (expiry.HasValue)
					{
						long valueOrDefault = expiry.GetValueOrDefault();
						value2 = Math.Min(valueOrDefault, num2);
					}
					else
					{
						value2 = num2;
					}
					cycle2.Expiry = value2;
					knownDuration = true;
					break;
				}
				default:
					cycle2.Expiry = null;
					break;
				}
			}
			else
			{
				if (!cycle2.WasActive && cycle2.Absent == 0)
				{
					continue;
				}
				cycle2.WasActive = false;
				if (!cycle2.Consumed)
				{
					long? expiry = cycle2.Expiry;
					if (expiry.HasValue)
					{
						long num3 = cycle2.Expiry.Value - nowMs;
						if (num3 <= marginMs)
						{
							cycle2.Consumed = true;
							cycle2.Absent = 0;
							expired = true;
							continue;
						}
						cycle2.Absent++;
						if (cycle2.Absent >= samplesRequired)
						{
							cycle2.Consumed = true;
							cycle2.Absent = 0;
							list.Add(new EarlyRemoval(text2, num3));
						}
						else
						{
							num = Math.Max(num, cycle2.Absent);
						}
						continue;
					}
				}
				cycle2.Absent = 0;
			}
		}
		return new RemovalSample(list, num, dictionary.Count > 0, knownDuration, expired, newCycle);
	}
}
