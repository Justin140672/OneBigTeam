using System.Threading.RateLimiting;

using HR.Api.Authentication;
using HR.Api.RateLimiting;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HR.Integration.Tests;

/// <summary>
/// CodeQL #60 and #68 (log forging from the request target): the 429 rate-limit rejection log
/// (<see cref="RateLimitRejectionLogging"/>, #60) and the revoked-session rejection log
/// (<see cref="SupabaseJwtBearerConfiguration.LogRevokedSessionRejected"/>, #68) must never include
/// caller-controlled request-target text (raw or percent-decoded path, query string). They identify
/// the request only by server-defined values: the registered rate-limit policy name, or the matched
/// endpoint's route template. Pure in-process tests — no WebApplicationFactory, database or fixture.
/// </summary>
public class RequestTargetLogForgingTests
{
    /// <summary>U+2028 LINE SEPARATOR — rendered as a line break by many log viewers.</summary>
    private const char LineSeparatorChar = (char)0x2028;

    private static readonly string LineSeparator = LineSeparatorChar.ToString();

    private const string HostileQuery ="?token=SECRET123&email=victim@example.com";

    private static readonly string[] ForbiddenFragments =
    [
        "\r",
        "\n",
        LineSeparator,
        "\u0000",
        "FORGED",
        "%0D",
        "SECRET123",
        "victim@example.com",
        "/api/login",
    ];

    public static TheoryData<string, PathString> HostilePaths => new()
    {
        { "raw CRLF", new PathString("/api/login\r\nFORGED log line") },
        { "raw LF", new PathString("/api/login\nFORGED") },
        { "decoded CRLF", PathString.FromUriComponent("/api/login%0D%0AFORGED") },
        { "percent text kept encoded", new PathString("/api/login%0D%0AFORGED") },
        { "unicode line separator", new PathString("/api/login" + LineSeparator + "FORGED") },
        { "NUL and ESC", new PathString("/api/login\u0000\u001bFORGED") },
    };

    // ── CodeQL #60: rate-limit rejection ──────────────────────────────────────────

    [Theory]
    [MemberData(nameof(HostilePaths))]
    public async Task RateLimitRejection_LogsOnlyThePolicyName_NeverTheRequestTarget(string scenario, PathString hostilePath)
    {
        var sink = new CapturedLogSink();
        var options = new RateLimiterOptions();
        options.AddIdentityRateLimiting(new ConfigurationBuilder().Build());

        using var loggerFactory = LoggerFactory.Create(b => b
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(new CapturingLoggerProvider(sink)));
        await using var services = new ServiceCollection()
            .AddSingleton(loggerFactory)
            .BuildServiceProvider();

        var ctx = new DefaultHttpContext { RequestServices = services };
        ctx.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new EnableRateLimitingAttribute(IdentityRateLimiting.LoginPolicy)),
            "test"));
        ctx.Request.Method = HttpMethods.Post;
        ctx.Request.Path = hostilePath;
        ctx.Request.QueryString = new QueryString(HostileQuery);

        Assert.NotNull(options.OnRejected);
        await options.OnRejected!(
            new OnRejectedContext { HttpContext = ctx, Lease = new FailedLease() },
            CancellationToken.None);

        var entry = Assert.Single(sink.Entries);
        Assert.Equal(IdentityRateLimiting.LoginPolicy, entry.RawStateValue("RateLimitPolicy"));
        Assert.Equal("identity-login", entry.RawStateValue("RateLimitPolicy"));
        AssertNoPathLikeStateKey(entry);
        AssertNoForbiddenContent(entry, scenario);
    }

    [Theory]
    [InlineData("evil\r\npolicy")]
    [InlineData("identity-login\n")]
    [InlineData("IDENTITY-LOGIN")]
    [InlineData("/api/login")]
    public void ResolvePolicyName_ReturnsUnknown_ForUnregisteredPolicyNames(string attackerPolicyName)
    {
        var ctx = new DefaultHttpContext();
        ctx.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new EnableRateLimitingAttribute(attackerPolicyName)),
            "test"));

        Assert.Equal(RateLimitRejectionLogging.UnknownPolicy, RateLimitRejectionLogging.ResolvePolicyName(ctx));
        Assert.Equal("unknown", RateLimitRejectionLogging.ResolvePolicyName(ctx));
    }

    [Fact]
    public void ResolvePolicyName_ReturnsUnknown_WhenNoEndpointMatched()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/login\r\nFORGED";

        Assert.Equal(RateLimitRejectionLogging.UnknownPolicy, RateLimitRejectionLogging.ResolvePolicyName(ctx));
    }

    [Fact]
    public void ResolvePolicyName_ReturnsUnknown_WhenEndpointHasNoRateLimitingMetadata()
    {
        var ctx = new DefaultHttpContext();
        ctx.SetEndpoint(new Endpoint(_ => Task.CompletedTask, EndpointMetadataCollection.Empty, "test"));

        Assert.Equal(RateLimitRejectionLogging.UnknownPolicy, RateLimitRejectionLogging.ResolvePolicyName(ctx));
    }

    [Theory]
    [InlineData(RateLimitRejectionLogging.ContactFormPolicy)]
    [InlineData(IdentityRateLimiting.LoginPolicy)]
    [InlineData(IdentityRateLimiting.SignUpPolicy)]
    [InlineData(IdentityRateLimiting.ForgotPasswordPolicy)]
    [InlineData(IdentityRateLimiting.ResendVerificationPolicy)]
    [InlineData(IdentityRateLimiting.AcceptInvitePolicy)]
    [InlineData(IdentityRateLimiting.ResetPasswordPolicy)]
    public void ResolvePolicyName_RoundTrips_EveryRegisteredPolicy(string knownPolicy)
    {
        var ctx = new DefaultHttpContext();
        ctx.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new EnableRateLimitingAttribute(knownPolicy)),
            "test"));

        Assert.Equal(knownPolicy, RateLimitRejectionLogging.ResolvePolicyName(ctx));
    }

    // ── CodeQL #68: revoked-session rejection ─────────────────────────────────────

    [Theory]
    [MemberData(nameof(HostilePaths))]
    public void RevokedSessionRejection_LogsRouteTemplate_NeverTheRequestTarget(string scenario, PathString hostilePath)
    {
        var sink = new CapturedLogSink();
        var logger = new CapturingLogger("SupabaseJwtBearer", sink);
        var sub = Guid.NewGuid();
        var issuedAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

        var ctx = new DefaultHttpContext();
        ctx.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("api/companies/{companyId}/employees"),
            0,
            EndpointMetadataCollection.Empty,
            "test"));
        ctx.Request.Path = hostilePath;
        ctx.Request.QueryString = new QueryString(HostileQuery);

        SupabaseJwtBearerConfiguration.LogRevokedSessionRejected(logger, ctx, sub, issuedAt);

        var entry = Assert.Single(sink.Entries);
        Assert.Equal("session_revoked", entry.RawStateValue("AuthEvent"));
        Assert.Equal(SupabaseJwtBearerConfiguration.SessionRevokedAuthEvent, entry.RawStateValue("AuthEvent"));
        Assert.Equal("api/companies/{companyId}/employees", entry.RawStateValue("RouteTemplate"));
        var rawSub = Assert.IsType<Guid>(entry.RawStateValue("Sub"));
        Assert.Equal(sub, rawSub);
        Assert.IsType<DateTimeOffset>(entry.RawStateValue("TokenIssuedAt"));
        AssertNoPathLikeStateKey(entry);
        AssertNoForbiddenContent(entry, scenario);
    }

    [Fact]
    public void RevokedSessionRejection_ReportsUnmatched_WhenNoRouteEndpointMatched()
    {
        var sink = new CapturedLogSink();
        var logger = new CapturingLogger("SupabaseJwtBearer", sink);

        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/login\r\nFORGED";
        ctx.Request.QueryString = new QueryString(HostileQuery);

        SupabaseJwtBearerConfiguration.LogRevokedSessionRejected(logger, ctx, Guid.NewGuid(), DateTimeOffset.UtcNow);

        var entry = Assert.Single(sink.Entries);
        Assert.Equal("(unmatched)", entry.RawStateValue("RouteTemplate"));
        Assert.Equal(SupabaseJwtBearerConfiguration.UnmatchedRoute, entry.RawStateValue("RouteTemplate"));
        AssertNoForbiddenContent(entry, "unmatched endpoint");
    }

    [Fact]
    public void RevokedSessionRejection_ReportsUnmatched_ForNonRouteEndpoint()
    {
        var sink = new CapturedLogSink();
        var logger = new CapturingLogger("SupabaseJwtBearer", sink);

        var ctx = new DefaultHttpContext();
        ctx.SetEndpoint(new Endpoint(_ => Task.CompletedTask, EndpointMetadataCollection.Empty, "/api/login\r\nFORGED"));

        SupabaseJwtBearerConfiguration.LogRevokedSessionRejected(logger, ctx, Guid.NewGuid(), DateTimeOffset.UtcNow);

        var entry = Assert.Single(sink.Entries);
        Assert.Equal(SupabaseJwtBearerConfiguration.UnmatchedRoute, entry.RawStateValue("RouteTemplate"));
        AssertNoForbiddenContent(entry, "non-route endpoint");
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private static void AssertNoPathLikeStateKey(CapturedEntry entry)
    {
        Assert.DoesNotContain(entry.State, p => p.Key == "Path");
        Assert.DoesNotContain(entry.State, p => p.Key.Contains("Path", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entry.State, p => p.Key.Contains("Query", StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertNoForbiddenContent(CapturedEntry entry, string scenario)
    {
        var text = entry.CombinedText;
        foreach (var fragment in ForbiddenFragments)
        {
            Assert.False(
                text.Contains(fragment, StringComparison.Ordinal),
                $"[{scenario}] log entry contained forbidden fragment {Escape(fragment)}: {Escape(text)}");
        }

        Assert.False(text.Any(char.IsControl), $"[{scenario}] log entry contained a control character: {Escape(text)}");
    }

    private static string Escape(string value) =>
        string.Concat(value.Select(c => char.IsControl(c) || c == LineSeparatorChar ? $"\\u{(int)c:x4}" : c.ToString()));

    /// <summary>Always-failed lease, as handed to OnRejected by the rate-limiting middleware.</summary>
    private sealed class FailedLease : RateLimitLease
    {
        public override bool IsAcquired => false;

        public override IEnumerable<string> MetadataNames => [];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }
    }

    /// <summary>One captured log entry: formatted message, raw structured state and active scope states.</summary>
    private sealed record CapturedEntry(
        string Category,
        LogLevel Level,
        string Message,
        IReadOnlyList<KeyValuePair<string, object?>> State,
        IReadOnlyList<object?> Scopes)
    {
        public object? RawStateValue(string key) => State.FirstOrDefault(p => p.Key == key).Value;

        /// <summary>Message + every state key/value + every scope (dictionary scopes flattened).</summary>
        public string CombinedText =>
            string.Join(" | ",
                new[] { Message }
                    .Concat(State.Select(p => $"{p.Key}={p.Value}"))
                    .Concat(Scopes.Select(DescribeScope)));

        private static string DescribeScope(object? scope) => scope switch
        {
            null => string.Empty,
            IEnumerable<KeyValuePair<string, object?>> pairs => string.Join(", ", pairs.Select(p => $"{p.Key}={p.Value}")),
            _ => scope.ToString() ?? string.Empty,
        };
    }

    private sealed class CapturedLogSink
    {
        private readonly object _gate = new();
        private readonly List<CapturedEntry> _entries = [];
        private readonly List<object?> _scopes = [];

        public IReadOnlyList<CapturedEntry> Entries
        {
            get { lock (_gate) return _entries.ToList(); }
        }

        public IDisposable PushScope(object? state)
        {
            lock (_gate) _scopes.Add(state);
            return new ScopeHandle(this, state);
        }

        public void Add(string category, LogLevel level, string message, IReadOnlyList<KeyValuePair<string, object?>> state)
        {
            lock (_gate) _entries.Add(new CapturedEntry(category, level, message, state, _scopes.ToList()));
        }

        private sealed class ScopeHandle(CapturedLogSink sink, object? state) : IDisposable
        {
            public void Dispose()
            {
                lock (sink._gate) sink._scopes.Remove(state);
            }
        }
    }

    private sealed class CapturingLogger(string category, CapturedLogSink sink) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => sink.PushScope(state);

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
            sink.Add(category, logLevel, formatter(state, exception), pairs);
        }
    }

    private sealed class CapturingLoggerProvider(CapturedLogSink sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, sink);

        public void Dispose()
        {
        }
    }
}
