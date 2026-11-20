using System;
using System.Collections.Generic;
using NinOnlineAutoAssist.Core.Models;

namespace NinOnlineAutoAssist.Live;

public static class FollowPathPlanner
{
	public static FollowStep? Next(LiveFrame frame, NavigationFrame grid, double stopDistance, FollowStep? previous = null)
	{
		if (!grid.DynamicHazardsKnown || grid.Map != frame.Map || !double.IsFinite(stopDistance) || stopDistance < 0.5)
		{
			return null;
		}
		TargetInfo target = frame.Snapshot.Target;
		if ((object)target == null || !target.IsAlive || !target.IsTargeted)
		{
			return null;
		}
		PlayerState player = frame.Snapshot.Player;
		if (!double.IsFinite(player.PositionX) || !double.IsFinite(player.PositionY) || !double.IsFinite(target.PositionX) || !double.IsFinite(target.PositionY))
		{
			return null;
		}
		GridPoint gridPoint = new GridPoint((int)Math.Floor(player.PositionX), (int)Math.Floor(player.PositionY));
		if (grid.Cell(gridPoint.X, gridPoint.Y) == NavigationCell.Unknown)
		{
			return null;
		}
		double num = target.PositionX - player.PositionX;
		double num2 = target.PositionY - player.PositionY;
		double num3 = Math.Sqrt(num * num + num2 * num2);
		if (num3 <= stopDistance)
		{
			return null;
		}
		double num4 = (num3 - stopDistance) / num3;
		FollowStep followStep = FollowSteering.Heading(num, num2, gridPoint, previous);
		if (grid.SafeStep(gridPoint, followStep) && FollowSteering.ClearSegment(grid, player.PositionX, player.PositionY, player.PositionX + num * num4, player.PositionY + num2 * num4))
		{
			return followStep;
		}
		GridPoint targetPoint = new GridPoint((int)Math.Floor(target.PositionX), (int)Math.Floor(target.PositionY));
		PriorityQueue<(GridPoint, double), double> priorityQueue = new PriorityQueue<(GridPoint, double), double>();
		Dictionary<GridPoint, GridPoint> dictionary = new Dictionary<GridPoint, GridPoint>();
		Dictionary<GridPoint, double> dictionary2 = new Dictionary<GridPoint, double> { [gridPoint] = 0.0 };
		priorityQueue.Enqueue((gridPoint, 0.0), Heuristic(gridPoint));
		dictionary[gridPoint] = gridPoint;
		GridPoint? gridPoint2 = null;
		GridPoint[] array = new GridPoint[8]
		{
			new GridPoint(0, -1),
			new GridPoint(0, 1),
			new GridPoint(-1, 0),
			new GridPoint(1, 0),
			new GridPoint(-1, -1),
			new GridPoint(1, -1),
			new GridPoint(-1, 1),
			new GridPoint(1, 1)
		};
		while (priorityQueue.Count > 0 && dictionary.Count <= 4096)
		{
			(GridPoint, double) tuple = priorityQueue.Dequeue();
			var (gridPoint3, _) = tuple;
			if (tuple.Item2 > dictionary2[gridPoint3])
			{
				continue;
			}
			int num5 = gridPoint3.X - targetPoint.X;
			int num6 = gridPoint3.Y - targetPoint.Y;
			if (gridPoint3 != gridPoint && (double)(num5 * num5 + num6 * num6) <= stopDistance * stopDistance && grid.Safe(gridPoint3))
			{
				gridPoint2 = gridPoint3;
				break;
			}
			GridPoint[] array2 = array;
			for (int i = 0; i < array2.Length; i++)
			{
				GridPoint gridPoint4 = array2[i];
				GridPoint gridPoint5 = new GridPoint(gridPoint3.X + gridPoint4.X, gridPoint3.Y + gridPoint4.Y);
				if (grid.SafeStep(gridPoint3, FollowStep.Between(gridPoint3, gridPoint5)))
				{
					double num7 = tuple.Item2 + ((gridPoint4.X != 0 && gridPoint4.Y != 0) ? Math.Sqrt(2.0) : 1.0);
					if (!dictionary2.TryGetValue(gridPoint5, out var value) || !(num7 >= value))
					{
						dictionary2[gridPoint5] = num7;
						dictionary[gridPoint5] = gridPoint3;
						priorityQueue.Enqueue((gridPoint5, num7), num7 + Heuristic(gridPoint5));
					}
				}
			}
		}
		if (gridPoint2.HasValue)
		{
			GridPoint gridPoint6 = gridPoint2.GetValueOrDefault();
			while (dictionary[gridPoint6] != gridPoint)
			{
				gridPoint6 = dictionary[gridPoint6];
			}
			return FollowStep.Between(gridPoint, gridPoint6);
		}
		return null;
		double Heuristic(GridPoint p)
		{
			return Math.Max(0.0, Math.Sqrt(Math.Pow(p.X - targetPoint.X, 2.0) + Math.Pow(p.Y - targetPoint.Y, 2.0)) - stopDistance);
		}
	}
}
