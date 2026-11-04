using NinOnlineAutoAssist.Core.Logging;
using System.Text;

namespace NinOnlineAutoAssist.Core.Logging;

/// <summary>Appends entries to a log file. Failures are swallowed (logging must never crash the engine).</summary>
public sealed class FileLogger : IAutoAssistLogger
{
    private readonly string _path;
    private readonly object _gate = new();

    public FileLogger(string path)
    {
        _path = path;
        try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); } catch { }
    }

    public void Log(LogLevel level, string message, Events.EventOrigin? origin = null)
    {
        try
        {
            var line = $"{DateTimeOffset.Now:O} [{level}] {(origin is { } o ? $"({o}) " : "")}{message}{Environment.NewLine}";
            lock (_gate) File.AppendAllText(_path, line, Encoding.UTF8);
        }
        catch { /* never crash the engine */ }
    }
}

/// <summary>Fan-out to multiple loggers.</summary>
public sealed class CompositeLogger : IAutoAssistLogger
{
    private readonly IReadOnlyList<IAutoAssistLogger> _sinks;
    public CompositeLogger(params IAutoAssistLogger[] sinks) => _sinks = sinks;
    public void Log(LogLevel level, string message, Events.EventOrigin? origin = null)
    {
        foreach (var sink in _sinks) sink.Log(level, message, origin);
    }
}
