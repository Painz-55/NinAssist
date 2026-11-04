namespace NinOnlineAutoAssist.Core.Logging;

public enum LogLevel { Debug, Info, Warning, Error }

/// <summary>
/// Minimal logger abstraction. Entries also carry the <see cref="EventOrigin"/>
/// classification when applicable so the UI can colour-code provenance.
/// </summary>
public interface IAutoAssistLogger
{
    void Log(LogLevel level, string message, Events.EventOrigin? origin = null);
}

/// <summary>In-memory ring buffer + sink event. Safe for UI subscription.</summary>
public sealed class MemoryLogger : IAutoAssistLogger
{
    private readonly object _gate = new();
    private readonly Queue<LogEntry> _entries = new();
    private readonly int _capacity;

    public event Action<LogEntry>? EntryLogged;

    public MemoryLogger(int capacity = 500) => _capacity = Math.Max(10, capacity);

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_gate) return _entries.ToArray();
    }

    public void Log(LogLevel level, string message, Events.EventOrigin? origin = null)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, message, origin);
        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > _capacity) _entries.Dequeue();
        }
        try { EntryLogged?.Invoke(entry); } catch { /* UI sinks must never break the engine */ }
    }
}

public sealed record LogEntry(DateTimeOffset At, LogLevel Level, string Message, Events.EventOrigin? Origin);

/// <summary>No-op logger for tests.</summary>
public sealed class NullLogger : IAutoAssistLogger
{
    public static readonly NullLogger Instance = new();
    public void Log(LogLevel level, string message, Events.EventOrigin? origin = null) { }
}
