using NinOnlineAutoAssist.Core;

namespace NinOnlineAutoAssist.Live;

public sealed record FollowStep(Direction Direction, GridPoint Destination, Direction SecondaryDirection = Direction.None)
{
	public GridPoint? Offset
	{
		get
		{
			GridPoint? gridPoint = Vector(Direction);
			if (gridPoint.HasValue)
			{
				GridPoint valueOrDefault = gridPoint.GetValueOrDefault();
				if (SecondaryDirection == Direction.None)
				{
					return valueOrDefault;
				}
				gridPoint = Vector(SecondaryDirection);
				if (gridPoint.HasValue)
				{
					GridPoint valueOrDefault2 = gridPoint.GetValueOrDefault();
					if (valueOrDefault.X * valueOrDefault2.X + valueOrDefault.Y * valueOrDefault2.Y == 0)
					{
						return new GridPoint(valueOrDefault.X + valueOrDefault2.X, valueOrDefault.Y + valueOrDefault2.Y);
					}
				}
				return null;
			}
			return null;
			static GridPoint? Vector(Direction d)
			{
				return d switch
				{
					Direction.Up => new GridPoint(0, -1),
					Direction.Down => new GridPoint(0, 1),
					Direction.Left => new GridPoint(-1, 0),
					Direction.Right => new GridPoint(1, 0),
					_ => null,
				};
			}
		}
	}

	public string MovementText
	{
		get
		{
			if (SecondaryDirection != Direction.None)
			{
				return $"{Direction}+{SecondaryDirection}";
			}
			return Direction.ToString();
		}
	}

	public FollowStep From(GridPoint start)
	{
		GridPoint? offset = Offset;
		if (offset.HasValue)
		{
			GridPoint valueOrDefault = offset.GetValueOrDefault();
			return this with
			{
				Destination = new GridPoint(start.X + valueOrDefault.X, start.Y + valueOrDefault.Y)
			};
		}
		return this;
	}

	public static FollowStep Between(GridPoint start, GridPoint end)
	{
		int num = end.X - start.X;
		int num2 = end.Y - start.Y;
		Direction direction = ((num2 < 0) ? Direction.Up : Direction.Down);
		Direction direction2 = ((num < 0) ? Direction.Left : Direction.Right);
		if (num2 != 0)
		{
			return new FollowStep(direction, end, (num != 0) ? direction2 : Direction.None);
		}
		return new FollowStep(direction2, end);
	}
}
