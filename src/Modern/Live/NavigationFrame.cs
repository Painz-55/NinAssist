using System.Collections.Generic;

namespace NinOnlineAutoAssist.Live;

public sealed record NavigationFrame(int Map, long CapturedAtMs, int OriginX, int OriginY, int Width, int Height, IReadOnlyList<NavigationCell> Cells, bool DynamicHazardsKnown, int VisibleHazards)
{
	public int IgnoredOwnShots { get; init; }

	public NavigationCell Cell(int x, int y)
	{
		if (Width > 0 && Height > 0 && Cells.Count == (long)Width * (long)Height && x >= OriginX && y >= OriginY && x < OriginX + Width && y < OriginY + Height)
		{
			return Cells[(y - OriginY) * Width + x - OriginX];
		}
		return NavigationCell.Unknown;
	}

	public bool Safe(GridPoint point)
	{
		if (Cell(point.X, point.Y) != NavigationCell.Open)
		{
			return false;
		}
		for (int i = -1; i <= 1; i++)
		{
			for (int j = -1; j <= 1; j++)
			{
				if (Cell(point.X + i, point.Y + j) == NavigationCell.Hazard)
				{
					return false;
				}
			}
		}
		return true;
	}

	public bool SafeStep(GridPoint start, FollowStep step)
	{
		GridPoint? offset = step.Offset;
		if (offset.HasValue)
		{
			GridPoint valueOrDefault = offset.GetValueOrDefault();
			GridPoint gridPoint = new GridPoint(start.X + valueOrDefault.X, start.Y + valueOrDefault.Y);
			if (step.Destination != gridPoint || !Safe(gridPoint))
			{
				return false;
			}
			if (valueOrDefault.X != 0 && valueOrDefault.Y != 0)
			{
				if (Safe(new GridPoint(start.X + valueOrDefault.X, start.Y)))
				{
					return Safe(new GridPoint(start.X, start.Y + valueOrDefault.Y));
				}
				return false;
			}
			return true;
		}
		return false;
	}
}
