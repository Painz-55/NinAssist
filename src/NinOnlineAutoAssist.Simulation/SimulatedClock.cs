using NinOnlineAutoAssist.Core.Abstractions;

namespace NinOnlineAutoAssist.Simulation;

/// <summary>Deterministic manual clock for tests and scripted scenarios.</summary>
public sealed class SimulatedClock : IMonotonicClock
{
    public long NowMs { get; private set; }

    public SimulatedClock(long startMs = 0) => NowMs = startMs;

    public void Advance(long deltaMs) => NowMs += deltaMs;
    public void Set(long nowMs) => NowMs = nowMs;
}
