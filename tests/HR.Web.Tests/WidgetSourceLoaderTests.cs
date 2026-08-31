using HR.Web.Components.Pages.Dashboards;
using HR.Web.Services;
using Microsoft.Extensions.Logging;

namespace HR.Web.Tests;

/// <summary>
/// DSH-03 — <see cref="WidgetSourceLoader"/> wraps a single widget data-source fetch so the caller
/// only ever sees Loaded / Failed: a throwing fetch is logged (with correlation context) and
/// swallowed, never rethrown and never surfaced with exception detail.
/// </summary>
public class WidgetSourceLoaderTests
{
    [Fact]
    public async Task SuccessfulFetch_ReturnsLoadedWithValueAndSourceName()
    {
        var logger = new CapturingLogger<WidgetSourceLoader>();
        var loader = new WidgetSourceLoader(logger);

        var result = await loader.LoadAsync("Recruitment summary", "Open vacancies", () => Task.FromResult(42));

        Assert.True(result.IsLoaded);
        Assert.Equal(42, result.ValueOrThrow);
        Assert.Equal("Open vacancies", result.SourceName);
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task ThrowingFetch_ReturnsFailed_DoesNotRethrow_AndLogsError()
    {
        var logger = new CapturingLogger<WidgetSourceLoader>();
        var loader = new WidgetSourceLoader(logger);

        WidgetResult<int> result = default;
        var ex = await Record.ExceptionAsync(async () =>
            result = await loader.LoadAsync<int>(
                "Recruitment summary",
                "Open vacancies",
                () => throw new InvalidOperationException("boom - upstream API 500")));

        Assert.Null(ex); // never rethrown
        Assert.True(result.IsFailed);
        Assert.Equal("Open vacancies", result.SourceName);

        var errorEntry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.IsType<InvalidOperationException>(errorEntry.Exception);
        // Correlation context is logged; the raw exception message is not part of the formatted text.
        Assert.Contains("Widget", errorEntry.Message);
        Assert.Contains("Source", errorEntry.Message);
        Assert.Contains("CorrelationId", errorEntry.Message);
        Assert.DoesNotContain("boom - upstream API 500", errorEntry.Message);
    }

    [Fact]
    public async Task ThrowingFetch_ForReferenceType_ReturnsFailedWithNullValue()
    {
        var loader = new WidgetSourceLoader(new CapturingLogger<WidgetSourceLoader>());

        var result = await loader.LoadAsync<string>(
            "Widget", "Source", () => throw new TimeoutException());

        Assert.True(result.IsFailed);
        Assert.Null(result.Value);
        Assert.Throws<InvalidOperationException>(() => result.ValueOrThrow);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
