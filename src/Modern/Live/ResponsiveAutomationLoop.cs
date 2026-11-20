using System;
using System.Threading;

namespace NinOnlineAutoAssist.Live;

public sealed class ResponsiveAutomationLoop : IDisposable
{
	private readonly AutoResetEvent _wake = new AutoResetEvent(initialState: false);

	private readonly Thread _thread;

	private int _stopping;

	public static int WaitMilliseconds(long? untilNextDeadlineMs)
	{
		switch (untilNextDeadlineMs)
		{
		default:
			return 5;
		case 1L:
		case 2L:
		case 3L:
		case 4L:
			return (int)untilNextDeadlineMs.Value;
		}
	}

	public ResponsiveAutomationLoop(Action step, Action<Exception> onFault, Func<long?>? untilNextDeadlineMs = null)
	{
		ResponsiveAutomationLoop responsiveAutomationLoop = this;
		_thread = new Thread((ThreadStart)delegate
		{
			try
			{
				using ResponsiveWaiter responsiveWaiter = new ResponsiveWaiter();
				while (Volatile.Read(in responsiveAutomationLoop._stopping) == 0)
				{
					step();
					responsiveWaiter.Wait(WaitMilliseconds(untilNextDeadlineMs?.Invoke()), responsiveAutomationLoop._wake);
				}
			}
			catch (Exception obj)
			{
				onFault(obj);
			}
		})
		{
			IsBackground = true,
			Name = "NinAutoAssist automation"
		};
		_thread.Start();
	}

	public void Wake()
	{
		_wake.Set();
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _stopping, 1) == 0)
		{
			_wake.Set();
			_thread.Join();
			_wake.Dispose();
		}
	}
}
