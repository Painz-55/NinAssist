using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Simulation;

namespace NinOnlineAutoAssist.Tests;

public sealed class ResponsiveAutomationTests
{
    [Fact]
    public void HoldUsesActualElapsedTimeWhenSleepOvershoots()
    {
        var clock = new SimulatedClock(0);
        var sleeps = 0;
        BoundedInputHold.Wait(40, clock, () => true, requested =>
        {
            Assert.InRange(requested, 1, 5);
            sleeps++;
            clock.Advance(15); // Windows scheduler may stretch each 5 ms request
        });
        Assert.Equal(3, sleeps); // old eight-sleep loop would take 120 ms
        Assert.Equal(45, clock.NowMs);
    }

    [Fact]
    public void HoldChecksPermissionBetweenSlices()
    {
        var clock = new SimulatedClock(0);
        var allowed = true;
        BoundedInputHold.Wait(40, clock, () => allowed, _ => { clock.Advance(5); allowed = false; });
        Assert.Equal(5, clock.NowMs);
    }

    [Fact]
    public void AlreadyRevokedPermissionNeverSleeps()
    {
        BoundedInputHold.Wait(40, new SimulatedClock(0), () => false, _ => Assert.Fail("Should not hold"));
    }

    [Fact]
    public void SingleLongOversleepDoesNotAddMoreDelay()
    {
        var clock = new SimulatedClock(0);
        var sleeps = 0;
        BoundedInputHold.Wait(40, clock, () => true, _ => { sleeps++; clock.Advance(80); });
        Assert.Equal(1, sleeps);
    }

    [Fact]
    public void WorkerRunsWithoutUiTicksAndStopsOnDispose()
    {
        using var stepped = new ManualResetEventSlim();
        var calls = 0;
        var callerThread = Environment.CurrentManagedThreadId;
        var workerThread = 0;
        Exception? fault = null;
        var loop = new ResponsiveAutomationLoop(() =>
        {
            workerThread = Environment.CurrentManagedThreadId;
            Interlocked.Increment(ref calls);
            stepped.Set();
        }, ex => fault = ex);
        try
        {
            Assert.True(stepped.Wait(TimeSpan.FromSeconds(5)));
            Assert.NotEqual(callerThread, workerThread);
            stepped.Reset();
            loop.Wake();
            Assert.True(stepped.Wait(TimeSpan.FromSeconds(5)));
        }
        finally { loop.Dispose(); }
        var stoppedAt = Volatile.Read(ref calls);
        Assert.Null(fault);
        Assert.True(stoppedAt > 0);
        loop.Dispose(); // repeated disposal is safe and cannot restart it
        Assert.Equal(stoppedAt, Volatile.Read(ref calls));
    }

    [Fact]
    public void WorkerFaultStopsFurtherStepsAndInvokesFailClosedHandler()
    {
        using var faulted = new ManualResetEventSlim();
        var calls = 0;
        var disabled = false;
        using var loop = new ResponsiveAutomationLoop(() =>
        {
            Interlocked.Increment(ref calls);
            throw new InvalidOperationException("test fault");
        }, _ => { disabled = true; faulted.Set(); });
        Assert.True(faulted.Wait(TimeSpan.FromSeconds(5)));
        loop.Wake();
        loop.Dispose();
        Assert.True(disabled);
        Assert.Equal(1, Volatile.Read(ref calls));
    }

    [Fact]
    public void SystemClockIsMonotonicAcrossInstances()
    {
        IMonotonicClock first = new SystemMonotonicClock(), second = new SystemMonotonicClock();
        var start = first.NowMs;
        Assert.True(second.NowMs >= start);
    }
}
