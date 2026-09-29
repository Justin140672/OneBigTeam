using Microsoft.Extensions.Logging;

namespace HR.Modules.Documents.Tests.Infrastructure;

internal sealed class FakeLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        Messages.Add(message);
        Entries.Add((logLevel, message, exception));
    }
}
