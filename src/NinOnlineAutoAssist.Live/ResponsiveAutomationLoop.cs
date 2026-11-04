namespace NinOnlineAutoAssist.Live;

/// <summary>
/// Single automation owner, independent from rendering. New samples wake it immediately;
/// bounded waits also service pending combo deadlines and safety checks between reads.
/// </summary>
public sealed class ResponsiveAutomationLoop : IDisposable
{
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _thread;
    private int _stopping;

    public static int WaitMilliseconds(long? untilNextDeadlineMs) => untilNextDeadlineMs is > 0 and < 5
        ? (int)untilNextDeadlineMs.Value : 5;

    public ResponsiveAutomationLoop(Action step, Action<Exception> onFault, Func<long?>? untilNextDeadlineMs = null)
    {
        _thread = new Thread(() =>
        {
            try
            {
                using var waiter = new ResponsiveWaiter();
                while (Volatile.Read(ref _stopping) == 0)
                {
                    step();
                    waiter.Wait(WaitMilliseconds(untilNextDeadlineMs?.Invoke()), _wake);
                }
            }
            catch (Exception ex) { onFault(ex); }
        }) { IsBackground = true, Name = "NinAutoAssist automation" };
        _thread.Start();
    }

    public void Wake() => _wake.Set();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0) return;
        _wake.Set();
        _thread.Join(); // caller must not own the step's synchronization lock
        _wake.Dispose();
    }
}
