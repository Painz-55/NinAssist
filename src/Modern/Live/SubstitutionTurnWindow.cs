namespace NinOnlineAutoAssist.Live;

public sealed class SubstitutionTurnWindow
{
	private LiveFrame? _previous;

	private long? _removedAt;

	public void Reset()
	{
		_previous = null;
		_removedAt = null;
	}

	public void Update(LiveFrame? frame, long nowMs, bool allowed)
	{
		if (!allowed || !Fresh(frame, nowMs))
		{
			Reset();
			return;
		}
		LiveFrame previous = _previous;
		if ((object)previous != null && (previous.Map != frame.Map || previous.Snapshot.Player.Id != frame.Snapshot.Player.Id || frame.CapturedAtMs < previous.CapturedAtMs || frame.CapturedAtMs - previous.CapturedAtMs > 500))
		{
			Reset();
		}
		else if (previous?.CapturedAtMs != frame.CapturedAtMs)
		{
			if (frame.SubstitutionActive)
			{
				_removedAt = null;
			}
			else if ((object)previous != null && previous.SubstitutionActive)
			{
				_removedAt = frame.CapturedAtMs;
			}
			_previous = frame;
		}
	}

	public bool IsOpen(LiveFrame? frame, long nowMs)
	{
		if (Fresh(frame, nowMs))
		{
			LiveFrame previous = _previous;
			if ((object)previous != null && frame.Map == previous.Map && frame.Snapshot.Player.Id == previous.Snapshot.Player.Id && frame.CapturedAtMs >= previous.CapturedAtMs && frame.CapturedAtMs - previous.CapturedAtMs <= 500)
			{
				if (!frame.SubstitutionActive || !previous.SubstitutionActive)
				{
					if (!frame.SubstitutionActive)
					{
						long? removedAt = _removedAt;
						if (removedAt.HasValue)
						{
							long valueOrDefault = removedAt.GetValueOrDefault();
							if (nowMs >= valueOrDefault)
							{
								return nowMs - valueOrDefault < 1000;
							}
						}
					}
					return false;
				}
				return true;
			}
		}
		return false;
	}

	private static bool Fresh(LiveFrame? frame, long nowMs)
	{
		if ((object)frame != null && frame.Snapshot.Player.IsAlive && nowMs >= frame.CapturedAtMs && nowMs - frame.CapturedAtMs <= 500)
		{
			long readDurationMs = frame.ReadDurationMs;
			if (readDurationMs >= 0)
			{
				return readDurationMs <= 500;
			}
			return false;
		}
		return false;
	}
}
