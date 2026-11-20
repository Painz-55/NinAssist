using System;
using System.Collections.Generic;
using System.Linq;

namespace NinOnlineAutoAssist.Live;

public static class NearbyPlayerMarkers
{
	public sealed record Marker(int Slot, string Name, double OffsetX, double OffsetY, bool Selected, long ObservationAgeMs = 0L, long MotionAgeMs = 0L, bool CurrentlyReported = true);

	public static IReadOnlyList<Marker> Select(MapPlayersFrame? list, LiveFrame? frame, long nowMs, double radiusTiles = 750.0, int limit = 256)
	{
		bool flag = (object)list == null || (object)frame == null || !list.IsCurrent(frame, nowMs) || nowMs - list.CapturedAtMs > 300 || !double.IsFinite(radiusTiles);
		if (!flag)
		{
			bool flag2 = ((radiusTiles <= 0.0 || radiusTiles > 750.0) ? true : false);
			flag = flag2;
		}
		bool flag3 = flag;
		if (!flag3)
		{
			bool flag2 = ((limit < 1 || limit > 512) ? true : false);
			flag3 = flag2;
		}
		if (flag3)
		{
			return Array.Empty<Marker>();
		}
		double x = frame.Snapshot.Player.PositionX;
		double y = frame.Snapshot.Player.PositionY;
		if (!double.IsFinite(x) || !double.IsFinite(y))
		{
			return Array.Empty<Marker>();
		}
		return Array.AsReadOnly((from p in (from p in list.Players.Where(delegate(MapPlayer p)
				{
					if (!p.IsLocal)
					{
						double? x2 = p.X;
						if (x2.HasValue)
						{
							double valueOrDefault = x2.GetValueOrDefault();
							x2 = p.Y;
							if (x2.HasValue)
							{
								double valueOrDefault2 = x2.GetValueOrDefault();
								if (double.IsFinite(valueOrDefault) && double.IsFinite(valueOrDefault2))
								{
									return Math.Pow(valueOrDefault - x, 2.0) + Math.Pow(valueOrDefault2 - y, 2.0) <= radiusTiles * radiusTiles;
								}
							}
						}
					}
					return false;
				})
				orderby (!p.IsSelected) ? 1 : 0, Math.Pow(p.X.Value - x, 2.0) + Math.Pow(p.Y.Value - y, 2.0)
				select p).Take(limit)
			select new Marker(p.Slot, p.Name, p.X.Value - x, p.Y.Value - y, p.IsSelected, 0L, 0L)).ToArray());
	}
}
