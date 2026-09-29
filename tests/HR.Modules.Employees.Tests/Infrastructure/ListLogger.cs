using Microsoft.Extensions.Logging;

namespace HR.Modules.Employees.Tests.Infrastructure;

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
