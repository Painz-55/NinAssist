using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Tests;

public sealed class ResponseDeadlineTests
{
    [Theory]
    [InlineData(null, 5)] [InlineData(-50L, 5)] [InlineData(0L, 5)]
    [InlineData(1L, 1)] [InlineData(2L, 2)] [InlineData(4L, 4)] [InlineData(5L, 5)] [InlineData(1000L, 5)]
    public void SchedulingHonorsImminentDeadlinesWithoutSpinningOnDeferredActions(long? remaining, int expected)
        => Assert.Equal(expected, ResponsiveAutomationLoop.WaitMilliseconds(remaining));

    [Fact]
    public void WaitIsInterruptibleByNewFrameAndWakeSignalIsConsumed()
    {
        using var wake = new AutoResetEvent(true);
        using var waiter = new ResponsiveWaiter();
        Assert.True(waiter.Wait(1000, wake));
        Assert.False(waiter.Wait(1, wake));
    }

    [Fact]
    public void WaitDurationRemainsBoundedAndInvalidDurationsAreRejected()
    {
        using var waiter = new ResponsiveWaiter();
        Assert.Throws<ArgumentOutOfRangeException>(() => waiter.Wait(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => waiter.Wait(1001));
        Assert.False(waiter.Wait(1)); waiter.Dispose();
    }

    [Fact]
    public void OrdinaryWaitFallbackRemainsInterruptibleWithoutHighResolutionSupport()
    {
        using var wake = new AutoResetEvent(true);
        using var waiter = new ResponsiveWaiter(preferHighResolution: false);
        Assert.False(waiter.UsesHighResolutionTimer);
        Assert.True(waiter.Wait(1000, wake)); Assert.False(waiter.Wait(1, wake));
    }

    [Fact]
    public void WorkerConsultsDeadlineAndStillStopsCleanly()
    {
        using var consulted = new ManualResetEventSlim();
        Exception? fault = null;
        using var loop = new ResponsiveAutomationLoop(() => { }, ex => fault = ex,
            () => { consulted.Set(); return 1; });
        Assert.True(consulted.Wait(TimeSpan.FromSeconds(5)));
        loop.Dispose(); Assert.Null(fault);
    }

    [Fact]
    public void DeadlineFailureStopsWorkerInsteadOfContinuingWithUntrustedState()
    {
        using var faulted = new ManualResetEventSlim();
        using var loop = new ResponsiveAutomationLoop(() => { }, _ => faulted.Set(), () => throw new InvalidOperationException("deadline"));
        Assert.True(faulted.Wait(TimeSpan.FromSeconds(5))); loop.Dispose();
    }
}
