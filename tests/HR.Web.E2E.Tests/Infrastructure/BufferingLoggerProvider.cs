using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace HR.Web.E2E.Tests.Infrastructure;

internal sealed class BufferingLoggerProvider(int capacity = 500) : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public ILogger CreateLogger(string categoryName) => new BufferLogger(categoryName, this);

    public IReadOnlyList<string> Snapshot() => _lines.ToArray();

    public void Dispose()
    {
    }

    private void Add(string line)
    {
        _lines.Enqueue(line);
        while (_lines.Count > capacity && _lines.TryDequeue(out _))
        {
        }
    }

    private sealed class BufferLogger(string category, BufferingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var text = formatter(state, exception);
            if (exception is not null) text += $" | {exception.GetType().Name}: {exception.Message}";
            owner.Add(DiagnosticText.Sanitize($"{DateTime.UtcNow:HH:mm:ss.fff} {logLevel} {category}: {text}", 1000));
        }
    }
}
