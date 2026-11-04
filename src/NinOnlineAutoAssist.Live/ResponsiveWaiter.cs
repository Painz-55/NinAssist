using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NinOnlineAutoAssist.Live;

/// <summary>Per-owner one-shot high resolution waits. No global timer-resolution change and no busy spin.</summary>
public sealed class ResponsiveWaiter : IDisposable
{
    private sealed class Timer : WaitHandle
    { public Timer(SafeWaitHandle handle) => SafeWaitHandle = handle; }
    private Timer? _timer;
    private WaitHandle? _lastWake;
    private WaitHandle[]? _waitSet;
    public bool UsesHighResolutionTimer => _timer is not null;

    public ResponsiveWaiter(bool preferHighResolution = true)
    {
        if (!preferHighResolution || !OperatingSystem.IsWindows()) return;
        try
        {
            var handle = CreateWaitableTimerExW(IntPtr.Zero, null, 0x2, 0x00100002); // HIGH_RESOLUTION; MODIFY_STATE | SYNCHRONIZE
            if (handle.IsInvalid) { handle.Dispose(); return; }
            _timer = new Timer(handle);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException)
        { /* Older Windows/non-Windows keeps ordinary bounded waits. */ }
    }

    /// <returns>True when interrupted by the supplied wake signal.</returns>
    public bool Wait(int milliseconds, WaitHandle? wake = null)
    {
        if (milliseconds is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        if (_timer is { } timer)
        {
            var due = -milliseconds * 10_000L;
            if (SetWaitableTimer(timer.SafeWaitHandle, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
            {
                if (wake is null) { timer.WaitOne(); return false; }
                if (!ReferenceEquals(wake, _lastWake)) { _lastWake = wake; _waitSet = new[] { wake, timer }; }
                return WaitHandle.WaitAny(_waitSet!) == 0;
            }
            timer.Dispose(); _timer = null; _lastWake = null; _waitSet = null;
        }
        if (wake is not null) return wake.WaitOne(milliseconds);
        Thread.Sleep(milliseconds); return false;
    }
    public void Dispose() { _timer?.Dispose(); _timer = null; _lastWake = null; _waitSet = null; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeWaitHandle CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint access);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long due, int period, IntPtr callback, IntPtr argument,
        [MarshalAs(UnmanagedType.Bool)] bool resume);
}
