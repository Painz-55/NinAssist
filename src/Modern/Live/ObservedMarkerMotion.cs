using System;
using System.Collections.Generic;
using System.Linq;

namespace NinOnlineAutoAssist.Live;

public sealed class ObservedMarkerMotion
{
	private sealed record Track(string Name, double StartX, double StartY, double EndX, double EndY, long StartedAtMs, int DurationMs);

	private readonly Dictionary<int, Track> _tracks = new Dictionary<int, Track>();

	public void Reset()
	{
		_tracks.Clear();
	}

	public void Set(int slot, string name, double x, double y, long nowMs, bool observed)
	{
		if (slot > 0 && !string.IsNullOrWhiteSpace(name) && double.IsFinite(x) && double.IsFinite(y))
		{
			if (!_tracks.TryGetValue(slot, out Track value) || value.Name != name)
			{
				_tracks[slot] = new Track(name, x, y, x, y, nowMs, 0);
			}
			else if (value.EndX != x || value.EndY != y)
			{
				(double, double) value2 = Position(slot, nowMs).Value;
				_tracks[slot] = new Track(name, value2.Item1, value2.Item2, x, y, nowMs, observed ? 130 : 90);
			}
		}
	}

	public (double X, double Y)? Position(int slot, long nowMs)
	{
		if (!_tracks.TryGetValue(slot, out Track value))
		{
			return null;
		}
		if (value.DurationMs == 0)
		{
			return (value.EndX, value.EndY);
		}
		double num = Math.Clamp((double)(nowMs - value.StartedAtMs) / (double)value.DurationMs, 0.0, 1.0);
		double num2 = num * num * (3.0 - 2.0 * num);
		return (value.StartX + (value.EndX - value.StartX) * num2, value.StartY + (value.EndY - value.StartY) * num2);
	}

	public void KeepOnly(IReadOnlyCollection<int> slots)
	{
		int[] array = _tracks.Keys.ToArray();
		foreach (int num in array)
		{
			if (!slots.Contains(num))
			{
				_tracks.Remove(num);
			}
		}
	}

	public bool IsAnimating(long nowMs)
	{
		return _tracks.Values.Any((Track t) => nowMs >= t.StartedAtMs && nowMs - t.StartedAtMs < t.DurationMs);
	}
}
