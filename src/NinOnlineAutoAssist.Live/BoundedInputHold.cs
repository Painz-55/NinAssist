using NinOnlineAutoAssist.Core.Abstractions;

namespace NinOnlineAutoAssist.Live;

/// <summary>Uses elapsed time, not a count of sleeps: Windows can oversleep each requested slice.</summary>
public static class BoundedInputHold
{
    public static void Wait(int durationMs, IMonotonicClock clock, Func<bool> allowed, Action<int>? sleep = null)
    {
        if (durationMs is < 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(durationMs));
        using var waiter = sleep is null ? new ResponsiveWaiter() : null;
        sleep ??= milliseconds => waiter!.Wait(milliseconds);
        var started = clock.NowMs;
        while (allowed())
        {
            var remaining = durationMs - (clock.NowMs - started);
            if (remaining <= 0) return;
            sleep((int)Math.Min(5, remaining));
        }
    }
}
