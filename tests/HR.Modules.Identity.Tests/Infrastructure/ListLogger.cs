using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Tests.Infrastructure;

/// <summary>
/// Captures every rendered log message (plus any exception text) so a test can assert that
/// sensitive values never reach the log output.
/// </summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

    public string Text => string.Join("\n", Messages);

    IDisposable? ILogger.BeginScope<TState>(TState state) => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var line = formatter(state, exception);
        if (exception is not null)
            line += " | " + exception;
        Messages.Add(line);
    }
}
