using System.Diagnostics;
using System.Text.Json;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>Scheduling benchmark only. Never opens the game or sends keyboard input.</summary>
public static class ResponseTimingProbe
{
    private static object Measure(Action wait)
    {
        var values = new List<double>();
        for (var i = 0; i < 20; i++)
        {
            var started = Stopwatch.GetTimestamp(); wait();
            values.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        values.Sort();
        return new { Samples = values.Count, MinMs = values[0], MedianMs = values[values.Count / 2], P95Ms = values[18], MaxMs = values[^1] };
    }
    public static int Run()
    {
        using var waiter = new ResponsiveWaiter();
        using var signal = new AutoResetEvent(false);
        var clock = new SystemMonotonicClock();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Warning = "Offline scheduling only; no game attachment/input and no server latency measurement.",
            HighResolutionTimer = waiter.UsesHighResolutionTimer,
            ThreadSleep5 = Measure(() => Thread.Sleep(5)),
            EventWait5 = Measure(() => signal.WaitOne(5)),
            ResponsiveWait5 = Measure(() => waiter.Wait(5)),
            LegacyHold40 = Measure(() => BoundedInputHold.Wait(40, clock, () => true, Thread.Sleep)),
            ResponsiveHold40 = Measure(() => BoundedInputHold.Wait(40, clock, () => true))
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
