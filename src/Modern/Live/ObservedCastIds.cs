using System.Collections.Generic;

namespace NinOnlineAutoAssist.Live;

public sealed class ObservedCastIds
{
	private string? _player;

	private int _map;

	private long _lastCapture = -1L;

	private (int Spell, long Started, long Total)? _candidate;

	private readonly List<int> _recent = new List<int>();

	public IReadOnlyList<int> Recent => _recent.ToArray();

	public void Reset()
	{
		_player = null;
		_map = 0;
		_lastCapture = -1L;
		_candidate = null;
		_recent.Clear();
	}

	public void Observe(LiveFrame? frame, long now)
	{
		(int, long, long) tuple;
		if ((object)frame != null && AutoCastTurnEngine.Fresh(frame, now))
		{
			CastObservation cast = frame.Cast;
			if ((object)cast != null && cast.Active && cast.Valid)
			{
				long? observedAtMs = cast.ObservedAtMs;
				if (observedAtMs.HasValue)
				{
					long valueOrDefault = observedAtMs.GetValueOrDefault();
					if (valueOrDefault < frame.CapturedAtMs || valueOrDefault > frame.CapturedAtMs + frame.ReadDurationMs || valueOrDefault > now)
					{
						goto IL_007f;
					}
				}
				if (!string.IsNullOrWhiteSpace(frame.PlayerName) && !string.IsNullOrWhiteSpace(frame.Snapshot.Player.Id))
				{
					if (_player != frame.Snapshot.Player.Id || _map != frame.Map || frame.CapturedAtMs < _lastCapture)
					{
						Reset();
						_player = frame.Snapshot.Player.Id;
						_map = frame.Map;
					}
					if (frame.CapturedAtMs <= _lastCapture)
					{
						return;
					}
					if (_lastCapture >= 0 && frame.CapturedAtMs - _lastCapture > 200)
					{
						_candidate = null;
					}
					_lastCapture = frame.CapturedAtMs;
					tuple = (cast.SpellId, cast.StartedAtGameMs, cast.TotalMs);
					(int, long, long)? candidate = _candidate;
					(int, long, long) tuple2 = tuple;
					bool hasValue = candidate.HasValue;
					if (hasValue)
					{
						if (hasValue)
						{
							(int, long, long) valueOrDefault2 = candidate.GetValueOrDefault();
							if (valueOrDefault2.Item1 != tuple2.Item1 || valueOrDefault2.Item2 != tuple2.Item2 || valueOrDefault2.Item3 != tuple2.Item3)
							{
								goto IL_01f7;
							}
						}
						_recent.Remove(cast.SpellId);
						_recent.Insert(0, cast.SpellId);
						if (_recent.Count > 20)
						{
							_recent.RemoveAt(_recent.Count - 1);
						}
					}
					goto IL_01f7;
				}
			}
		}
		goto IL_007f;
		IL_01f7:
		_candidate = tuple;
		return;
		IL_007f:
		_candidate = null;
	}
}
