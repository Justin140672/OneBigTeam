using System.Collections;
using System.Text;
using Microsoft.Extensions.Logging;

namespace HR.Infrastructure.Tests.Infrastructure;

internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<object?> _activeScopes = [];

    public List<CapturedLogEntry> Entries { get; } = [];

    public string AllText => string.Join("\n", Entries.Select(e => e.AllText));

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        _activeScopes.Add(state);
        return new ScopeHandle(() => _activeScopes.Remove(state));
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var stateValues = new List<KeyValuePair<string, string?>>();
        if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            foreach (var pair in pairs)
                stateValues.Add(new(pair.Key, Describe(pair.Value)));
        }
        else
        {
            stateValues.Add(new("(state)", Describe(state)));
        }

        var scopes = _activeScopes.Select(Describe).ToList();

        Entries.Add(new CapturedLogEntry(
            logLevel,
            formatter(state, exception),
            stateValues,
            scopes,
            exception is null ? null : DescribeException(exception)));
    }

    private static string? Describe(object? value) => value switch
    {
        null => null,
        string s => s,
        IEnumerable<KeyValuePair<string, object?>> kvps => string.Join(", ", kvps.Select(p => $"{p.Key}={Describe(p.Value)}")),
        IEnumerable enumerable => string.Join(", ", enumerable.Cast<object?>().Select(Describe)),
        _ => value.ToString(),
    };

    private static string DescribeException(Exception exception)
    {
        var text = new StringBuilder(exception.ToString());
        for (var current = exception; current is not null; current = current.InnerException)
        {
            foreach (DictionaryEntry entry in current.Data)
                text.Append('\n').Append(entry.Key).Append('=').Append(entry.Value);

            foreach (var property in current.GetType().GetProperties())
            {
                if (property.GetIndexParameters().Length > 0 || property.Name is nameof(Exception.TargetSite))
                    continue;
                try
                {
                    text.Append('\n').Append(property.Name).Append('=').Append(property.GetValue(current));
                }
                catch
                {
                }
            }
        }

        return text.ToString();
    }

    private sealed class ScopeHandle(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}

internal sealed record CapturedLogEntry(
    LogLevel Level,
    string Message,
    IReadOnlyList<KeyValuePair<string, string?>> State,
    IReadOnlyList<string?> Scopes,
    string? ExceptionText)
{
    public string? StateValue(string key) => State.FirstOrDefault(p => p.Key == key).Value;

    public string AllText =>
        string.Join("\n",
            new[] { Message, ExceptionText }
                .Concat(State.Select(p => $"{p.Key}={p.Value}"))
                .Concat(Scopes)
                .Where(s => s is not null));
}
