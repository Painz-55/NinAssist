using System;
using System.Collections.Generic;
using System.Linq;
using NinOnlineAutoAssist.Core;

namespace NinOnlineAutoAssist.Live;

public sealed class HeldDirectionController : IDisposable
{
	private readonly Func<Direction, bool> _isDown;

	private readonly Func<Direction, bool> _down;

	private readonly Func<Direction, bool> _up;

	private readonly HashSet<Direction> _owned = new HashSet<Direction>();

	public Direction Held => _owned.FirstOrDefault();

	public bool Faulted { get; private set; }

	public bool Owns(Direction direction)
	{
		return _owned.Contains(direction);
	}

	public HeldDirectionController(Func<Direction, bool> isDown, Func<Direction, bool> down, Func<Direction, bool> up)
	{
		_isDown = isDown;
		_down = down;
		_up = up;
	}

	public bool Set(Direction direction, bool allowed, Direction secondary = Direction.None)
	{
		FollowStep followStep = new FollowStep(direction, default(GridPoint), secondary);
		if (!allowed || !followStep.Offset.HasValue || Faulted)
		{
			Release();
			return false;
		}
		Direction[] desired = ((secondary != Direction.None) ? new Direction[2] { direction, secondary } : new Direction[1] { direction });
		try
		{
			if (desired.Any((Direction d) => !_owned.Contains(d) && _isDown(d)))
			{
				Release();
				return false;
			}
			Direction[] array = _owned.Where((Direction d) => !desired.Contains(d)).ToArray();
			foreach (Direction direction2 in array)
			{
				if (_up(direction2))
				{
					_owned.Remove(direction2);
					continue;
				}
				Faulted = true;
				Release();
				return false;
			}
			foreach (Direction item in desired.Where((Direction d) => !_owned.Contains(d)))
			{
				if (!_down(item))
				{
					Release();
					return false;
				}
				_owned.Add(item);
			}
			return true;
		}
		catch
		{
			Release();
			throw;
		}
	}

	public void Release()
	{
		Direction[] array = _owned.ToArray();
		foreach (Direction direction in array)
		{
			if (_up(direction))
			{
				_owned.Remove(direction);
			}
			else
			{
				Faulted = true;
			}
		}
	}

	public void Dispose()
	{
		Release();
	}
}
