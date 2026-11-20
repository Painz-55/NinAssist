using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace NinOnlineAutoAssist.Live;

public sealed class ResponsiveWaiter : IDisposable
{
	private sealed class Timer : WaitHandle
	{
		public Timer(SafeWaitHandle handle)
		{
			base.SafeWaitHandle = handle;
		}
	}

	private Timer? _timer;

	private WaitHandle? _lastWake;

	private WaitHandle[]? _waitSet;

	public bool UsesHighResolutionTimer => _timer != null;

	public ResponsiveWaiter(bool preferHighResolution = true)
	{
		if (!preferHighResolution || !OperatingSystem.IsWindows())
		{
			return;
		}
		try
		{
			SafeWaitHandle safeWaitHandle = CreateWaitableTimerExW(IntPtr.Zero, null, 2u, 1048578u);
			if (safeWaitHandle.IsInvalid)
			{
				safeWaitHandle.Dispose();
			}
			else
			{
				_timer = new Timer(safeWaitHandle);
			}
		}
		catch (Exception ex) when (((ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is PlatformNotSupportedException) ? 1 : 0) != 0)
		{
		}
	}

	public bool Wait(int milliseconds, WaitHandle? wake = null)
	{
		if ((milliseconds < 1 || milliseconds > 1000) ? true : false)
		{
			throw new ArgumentOutOfRangeException("milliseconds");
		}
		Timer timer = _timer;
		if (timer != null)
		{
			long due = (long)(-milliseconds) * 10000L;
			if (SetWaitableTimer(timer.SafeWaitHandle, ref due, 0, IntPtr.Zero, IntPtr.Zero, resume: false))
			{
				if (wake == null)
				{
					timer.WaitOne();
					return false;
				}
				if (wake != _lastWake)
				{
					_lastWake = wake;
					_waitSet = new WaitHandle[2] { wake, timer };
				}
				return WaitHandle.WaitAny(_waitSet) == 0;
			}
			timer.Dispose();
			_timer = null;
			_lastWake = null;
			_waitSet = null;
		}
		if (wake != null)
		{
			return wake.WaitOne(milliseconds);
		}
		Thread.Sleep(milliseconds);
		return false;
	}

	public void Dispose()
	{
		_timer?.Dispose();
		_timer = null;
		_lastWake = null;
		_waitSet = null;
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
	private static extern SafeWaitHandle CreateWaitableTimerExW(nint attributes, string? name, uint flags, uint access);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long due, int period, nint callback, nint argument, [MarshalAs(UnmanagedType.Bool)] bool resume);
}
