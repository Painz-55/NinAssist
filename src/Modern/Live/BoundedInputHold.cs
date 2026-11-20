using System;
using NinOnlineAutoAssist.Core.Abstractions;

namespace NinOnlineAutoAssist.Live;

public static class BoundedInputHold
{
	public static void Wait(int durationMs, IMonotonicClock clock, Func<bool> allowed, Action<int>? sleep = null)
	{
		if ((durationMs < 0 || durationMs > 1000) ? true : false)
		{
			throw new ArgumentOutOfRangeException("durationMs");
		}
		ResponsiveWaiter waiter = ((sleep == null) ? new ResponsiveWaiter() : null);
		try
		{
			if (sleep == null)
			{
				sleep = delegate(int milliseconds)
				{
					waiter.Wait(milliseconds);
				};
			}
			long nowMs = clock.NowMs;
			while (allowed())
			{
				long num = durationMs - (clock.NowMs - nowMs);
				if (num <= 0)
				{
					break;
				}
				sleep((int)Math.Min(5L, num));
			}
		}
		finally
		{
			if (waiter != null)
			{
				((IDisposable)waiter).Dispose();
			}
		}
	}
}
