using System.Text.RegularExpressions;
using HR.Infrastructure.Logging;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HR.Infrastructure.Tests;

/// <summary>
/// CodeQL #63-#65, #67 (log forging via the caller-supplied X-Correlation-ID header): the
/// correlation id flows from <see cref="CorrelationIdMiddleware"/> into the ambient
/// <see cref="IExecutionContext"/> and from there into every log line and log scope written by
/// <see cref="IntegrationEventPublisher"/> (#67) and the attachment cleanup paths (#63-#65). These
/// tests pin the length/character allow-list (including the <c>\z</c> anchor fix — "abc" + LF used
/// to be accepted by a <c>$</c>-anchored regex) and prove end to end that a hostile header is
/// replaced by a server-generated GUID before it can reach any log entry.
/// </summary>
public class CorrelationIdLogSafetyTests
{
    private const char LineSeparator = (char)0x2028;

    private static readonly Regex SafeCorrelationId = new(@"^[A-Za-z0-9._:-]{1,128}\z");

    public static TheoryData<string?> RejectedValues => new()
    {
        "abc\n",
        "abc\r\n",
        "a\rb",
        "a\nb",
        "a\u0000b",
        "a\tb",
        "a b",
        "a%0D%0Ab",
        "a" + LineSeparator + "b",
        "",
        "   ",
        null,
        new string('a', 129),
    };

    public static TheoryData<string> AcceptedValues => new()
    {
        "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
        "e2e-3f2504e0-4f89-11d3-9a0c-0305e82c3301",
        new string('a', 128),
        "trace.id:1_2-3",
    };

    [Theory]
    [MemberData(nameof(RejectedValues))]
    public void IsAcceptable_Rejects_Malformed_Or_Oversized_Values(string? supplied)
    {
        Assert.False(CorrelationIdMiddleware.IsAcceptable(supplied));
    }

    [Theory]
    [MemberData(nameof(AcceptedValues))]
    public void IsAcceptable_Accepts_Values_Within_Length_And_Character_Policy(string supplied)
    {
        Assert.True(CorrelationIdMiddleware.IsAcceptable(supplied));
    }

    [Theory]
    [InlineData("abc\nFORGED")]
    [InlineData("abc\r\nFORGED")]
    public async Task Middleware_Replaces_Hostile_Header_With_Generated_Guid(string hostile)
    {
        var accessor = new ExecutionContextAccessor();
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[CorrelationIdMiddleware.HeaderName] = hostile;

        string? observed = null;
        var middleware = new CorrelationIdMiddleware(_ =>
        {
            observed = accessor.Current!.CorrelationId;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(ctx, accessor);

        Assert.NotNull(observed);
        Assert.True(Guid.TryParse(observed, out _), $"Expected a generated GUID, got '{observed}'.");
        Assert.NotEqual(hostile, observed);
        Assert.DoesNotContain("FORGED", observed);
        Assert.Equal(observed, ctx.Items[CorrelationIdMiddleware.ItemsKey]);
    }

    [Fact]
    public async Task Middleware_Flows_Valid_Prefixed_Header_Verbatim()
    {
        var supplied = "e2e-" + Guid.NewGuid().ToString("D");
        var accessor = new ExecutionContextAccessor();
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[CorrelationIdMiddleware.HeaderName] = supplied;

        string? observed = null;
        var middleware = new CorrelationIdMiddleware(_ =>
        {
            observed = accessor.Current!.CorrelationId;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(ctx, accessor);

        Assert.Equal(supplied, observed);
        Assert.Equal(supplied, ctx.Items[CorrelationIdMiddleware.ItemsKey]);
    }

    public static TheoryData<string, bool> PublisherHeaders => new()
    {
        { "abc\nFORGED", false },
        { "abc\r\nFORGED", false },
        { "e2e-6fa459ea-ee8a-3ca4-894e-db77e160355e", true },
    };

    [Theory]
    [MemberData(nameof(PublisherHeaders))]
    public async Task IntegrationEventPublisher_FailureLog_Carries_Only_The_Validated_CorrelationId(
        string suppliedHeader, bool expectVerbatim)
    {
        var accessor = new ExecutionContextAccessor();
        var logger = new CapturingLogger<IntegrationEventPublisher>();
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationEventHandler<CorrelationLogSafetyTestEvent>, ThrowingTestEventHandler>();
        await using var provider = services.BuildServiceProvider();

        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[CorrelationIdMiddleware.HeaderName] = suppliedHeader;

        string? resolvedCorrelationId = null;
        Guid requestMessageId = Guid.Empty;

        var middleware = new CorrelationIdMiddleware(async _ =>
        {
            resolvedCorrelationId = accessor.Current!.CorrelationId;
            requestMessageId = accessor.Current.MessageId;

            var publisher = new IntegrationEventPublisher(provider, logger, accessor);
            await publisher.PublishAsync(new CorrelationLogSafetyTestEvent(), CancellationToken.None);
        });

        await middleware.InvokeAsync(ctx, accessor);

        Assert.NotNull(resolvedCorrelationId);
        if (expectVerbatim)
            Assert.Equal(suppliedHeader, resolvedCorrelationId);
        else
            Assert.True(Guid.TryParse(resolvedCorrelationId, out _));

        var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);

        var correlationId = Assert.IsType<string>(entry.RawStateValue("CorrelationId"));
        Assert.Equal(resolvedCorrelationId, correlationId);
        Assert.Matches(SafeCorrelationId, correlationId);

        var messageId = Assert.IsType<Guid>(entry.RawStateValue("MessageId"));
        Assert.NotEqual(Guid.Empty, messageId);
        Assert.NotEqual(requestMessageId, messageId);

        var causationId = Assert.IsType<Guid>(entry.RawStateValue("CausationId"));
        Assert.Equal(requestMessageId, causationId);

        var scope = Assert.Single(entry.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>());
        var scopeValues = scope.ToDictionary(p => p.Key, p => p.Value);
        Assert.Equal(correlationId, scopeValues["CorrelationId"]);
        Assert.Equal(messageId, Assert.IsType<Guid>(scopeValues["MessageId"]));
        Assert.Equal(causationId, Assert.IsType<Guid>(scopeValues["CausationId"]));

        var text = entry.CombinedText;
        Assert.DoesNotContain("\r", text);
        Assert.DoesNotContain("\n", text);
        Assert.DoesNotContain("FORGED", text);
    }

    private sealed record CorrelationLogSafetyTestEvent : IIntegrationEvent;

    private sealed class ThrowingTestEventHandler : IIntegrationEventHandler<CorrelationLogSafetyTestEvent>
    {
        public Task HandleAsync(CorrelationLogSafetyTestEvent integrationEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated handler failure");
    }

    private sealed record CapturedEntry(
        LogLevel Level,
        string Message,
        IReadOnlyList<KeyValuePair<string, object?>> State,
        IReadOnlyList<object?> Scopes)
    {
        public object? RawStateValue(string key) => State.FirstOrDefault(p => p.Key == key).Value;

        /// <summary>Message + state + scopes (exception text deliberately excluded — it is not
        /// request-derived here).</summary>
        public string CombinedText =>
            string.Join(" | ",
                new[] { Message }
                    .Concat(State.Select(p => $"{p.Key}={p.Value}"))
                    .Concat(Scopes.Select(s => s is IEnumerable<KeyValuePair<string, object?>> pairs
                        ? string.Join(", ", pairs.Select(p => $"{p.Key}={p.Value}"))
                        : s?.ToString() ?? string.Empty)));
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<object?> _scopes = [];

        public List<CapturedEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            _scopes.Add(state);
            return new ScopeHandle(() => _scopes.Remove(state));
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var pairs = state is IEnumerable<KeyValuePair<string, object?>> kvps
                ? kvps.ToList()
                : [new KeyValuePair<string, object?>("(state)", state)];
            Entries.Add(new CapturedEntry(logLevel, formatter(state, exception), pairs, _scopes.ToList()));
        }

        private sealed class ScopeHandle(Action dispose) : IDisposable
        {
            public void Dispose() => dispose();
        }
    }
}
