using Microsoft.Extensions.Logging;

namespace SimCoach.Reference.Tests;

/// <summary>Non-generic <see cref="ILogger"/> that captures entries so tests can assert on level + message
/// (<see cref="ComputeSession"/> takes the non-generic logger).</summary>
internal sealed class CollectingLogger : ILogger
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Snapshot()
    {
        lock (_entries)
        {
            return [.. _entries];
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_entries)
        {
            _entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
