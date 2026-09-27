using System.Collections;
using System.Text;
using Microsoft.Extensions.Logging;

namespace HR.Infrastructure.Tests.Infrastructure;

/// <summary>
/// Mirrors HR.Modules.Identity.Tests CapturingLogger (CodeQL #61). Strict capture for asserting that personal
/// data (e.g. an email address) never reaches operational logs by ANY channel. For every entry it
/// records the formatted message, every structured state key/value (including the raw
/// <c>{OriginalFormat}</c> template), the full exception text (<c>ToString()</c>, which includes
/// inner exceptions) plus exception <c>Data</c> and public property values, and every active
/// scope's state — so a value hidden in a destructured property, a scope, or an exception
/// message is still caught by <see cref="AllText"/>.
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<object?> _activeScopes = [];

    public List<CapturedLogEntry> Entries { get; } = [];

    /// <summary>Every captured string from every channel, newline-joined.</summary>
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

            // Structured exception destructurers (e.g. Serilog.Exceptions) emit public properties,
            // so an address held in a property such as EmailAlreadyRegisteredException.Email counts.
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
                    // A throwing property getter is irrelevant to what would be logged.
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
