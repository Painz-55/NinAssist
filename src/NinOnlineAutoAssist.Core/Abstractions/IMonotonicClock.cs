namespace NinOnlineAutoAssist.Core.Abstractions;

/// <summary>
/// Monotonic clock (never goes backwards, immune to wall-clock changes).
/// All timing logic in the engines depends on this abstraction so tests can
/// advance time deterministically.
/// </summary>
public interface IMonotonicClock
{
    /// <summary>Milliseconds from an unspecified epoch, monotonically increasing.</summary>
    long NowMs { get; }
}

/// <summary>Real-time monotonic clock backed by Stopwatch.</summary>
public sealed class SystemMonotonicClock : IMonotonicClock
{
    private static readonly long Epoch = System.Diagnostics.Stopwatch.GetTimestamp();
    public long NowMs => (long)System.Diagnostics.Stopwatch.GetElapsedTime(Epoch).TotalMilliseconds;
}
