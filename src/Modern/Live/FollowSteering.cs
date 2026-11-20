using System;
using NinOnlineAutoAssist.Core;

namespace NinOnlineAutoAssist.Live;

public static class FollowSteering
{
	public static FollowStep Heading(double dx, double dy, GridPoint start, FollowStep? previous = null)
	{
		if (!double.IsFinite(dx) || !double.IsFinite(dy))
		{
			return new FollowStep(Direction.None, start);
		}
		double num = Math.Abs(dx);
		double num2 = Math.Abs(dy);
		int num3 = ((!(num < 0.1)) ? Math.Sign(dx) : 0);
		int num4 = ((!(num2 < 0.1)) ? Math.Sign(dy) : 0);
		if (num3 == 0 && num4 == 0)
		{
			return new FollowStep(Direction.None, start);
		}
		if (num3 != 0 && num4 != 0)
		{
			double num5 = Math.Min(num, num2) / Math.Max(num, num2);
			double num6 = Math.Sqrt(2.0) - 1.0;
			GridPoint? gridPoint = previous?.Offset;
			if (gridPoint.HasValue)
			{
				GridPoint valueOrDefault = gridPoint.GetValueOrDefault();
				if (valueOrDefault.X == num3 && valueOrDefault.Y == num4)
				{
					num6 = 0.3;
				}
				else if ((num >= num2 && valueOrDefault.X == num3 && valueOrDefault.Y == 0) || (num2 > num && valueOrDefault.Y == num4 && valueOrDefault.X == 0))
				{
					num6 = 0.5;
				}
			}
			if (num5 < num6)
			{
				if (num >= num2)
				{
					num4 = 0;
				}
				else
				{
					num3 = 0;
				}
			}
		}
		return FollowStep.Between(start, new GridPoint(start.X + num3, start.Y + num4));
	}

	public static bool ClearSegment(NavigationFrame grid, double x, double y, double endX, double endY)
	{
		if (!grid.DynamicHazardsKnown || !double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(endX) || !double.IsFinite(endY))
		{
			return false;
		}
		GridPoint gridPoint = new GridPoint((int)Math.Floor(x), (int)Math.Floor(y));
		GridPoint gridPoint2 = new GridPoint((int)Math.Floor(endX), (int)Math.Floor(endY));
		if (!grid.Safe(gridPoint))
		{
			return false;
		}
		double value = endX - x;
		double value2 = endY - y;
		int num = Math.Sign(value);
		int num2 = Math.Sign(value2);
		double num3 = ((num == 0) ? double.PositiveInfinity : (1.0 / Math.Abs(value)));
		double num4 = ((num2 == 0) ? double.PositiveInfinity : (1.0 / Math.Abs(value2)));
		double num5 = ((num == 0) ? double.PositiveInfinity : (((num > 0) ? ((double)(gridPoint.X + 1) - x) : (x - (double)gridPoint.X)) / Math.Abs(value)));
		double num6 = ((num2 == 0) ? double.PositiveInfinity : (((num2 > 0) ? ((double)(gridPoint.Y + 1) - y) : (y - (double)gridPoint.Y)) / Math.Abs(value2)));
		for (int i = 0; i < 4096; i++)
		{
			if (gridPoint == gridPoint2)
			{
				return true;
			}
			bool flag = num5 <= num6 + 1E-10;
			bool flag2 = num6 <= num5 + 1E-10;
			GridPoint gridPoint3 = new GridPoint(gridPoint.X + (flag ? num : 0), gridPoint.Y + (flag2 ? num2 : 0));
			if (!grid.SafeStep(gridPoint, FollowStep.Between(gridPoint, gridPoint3)))
			{
				return false;
			}
			gridPoint = gridPoint3;
			if (flag)
			{
				num5 += num3;
			}
			if (flag2)
			{
				num6 += num4;
			}
		}
		return false;
	}
}
