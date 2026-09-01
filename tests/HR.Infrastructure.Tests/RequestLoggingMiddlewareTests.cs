using HR.Infrastructure.Logging;
using Microsoft.AspNetCore.Http;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Microsoft.Extensions.Logging;
using Serilog.Extensions.Logging;

namespace HR.Infrastructure.Tests;

/// <summary>
/// Ticket 3 / CodeQL "Log entries created from user input": user-controlled request data
/// (path, method, route values) must never be able to forge log lines, and credentials
/// must never reach the request log.
/// </summary>
public class RequestLoggingMiddlewareTests
{
    private const char Sep2028 = (char)0x2028;
    private const char Sep2029 = (char)0x2029;

    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private static (RequestLoggingMiddleware Middleware, CapturingSink Sink) Build(RequestDelegate next)
    {
        var sink = new CapturingSink();
        var serilog = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .WriteTo.Sink(sink)
            .CreateLogger();
        ILoggerFactory factory = new SerilogLoggerFactory(serilog);
        var logger = factory.CreateLogger<RequestLoggingMiddleware>();
        return (new RequestLoggingMiddleware(next, logger), sink);
    }

    private static HttpContext ContextFor(string path, string method = "GET")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Request.Method = method;
        return ctx;
    }

    private static void AssertNoForgedLine(LogEvent evt)
    {
        var rendered = evt.RenderMessage();
        Assert.DoesNotContain('\r', rendered);
        Assert.DoesNotContain('\n', rendered);
        Assert.DoesNotContain(Sep2028, rendered);
        Assert.DoesNotContain(Sep2029, rendered);
    }

    [Fact]
    public async Task Crlf_in_path_cannot_forge_a_log_line()
    {
        var (mw, sink) = Build(_ => Task.CompletedTask);

        await mw.InvokeAsync(ContextFor("/api/users\r\nFATAL fake breach detected"));

        Assert.NotEmpty(sink.Events);
        foreach (var evt in sink.Events)
        {
            AssertNoForgedLine(evt);
            Assert.DoesNotContain("\nFATAL fake breach", evt.RenderMessage());
        }
    }

    [Theory]
    [InlineData("/a\rb")]
    [InlineData("/a\nb")]
    [InlineData("/a\r\nb")]
    [InlineData("/a\n\rINFO forged\r\n")]
    public async Task Control_characters_are_stripped_from_logged_path(string path)
    {
        var (mw, sink) = Build(_ => Task.CompletedTask);

        await mw.InvokeAsync(ContextFor(path));

        Assert.NotEmpty(sink.Events);
        foreach (var evt in sink.Events)
        {
            AssertNoForgedLine(evt);
        }
    }

    [Fact]
    public async Task Unicode_line_separators_are_stripped_from_logged_path()
    {
        var (mw, sink) = Build(_ => Task.CompletedTask);

        await mw.InvokeAsync(ContextFor($"/a{Sep2028}b{Sep2029}c"));

        Assert.NotEmpty(sink.Events);
        foreach (var evt in sink.Events)
        {
            AssertNoForgedLine(evt);
        }
    }

    [Fact]
    public async Task Values_are_emitted_as_structured_properties()
    {
        var (mw, sink) = Build(ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        await mw.InvokeAsync(ContextFor("/api/companies", "POST"));

        var completion = sink.Events.Last();
        Assert.Equal("POST", completion.Properties["Method"].ToString().Trim('"'));
        Assert.Equal("/api/companies", completion.Properties["Path"].ToString().Trim('"'));
        Assert.Equal("200", completion.Properties["StatusCode"].ToString());
        Assert.True(completion.Properties.ContainsKey("ElapsedMs"));
    }

    [Fact]
    public async Task Authorization_and_cookie_headers_never_enter_the_request_log()
    {
        var (mw, sink) = Build(_ => Task.CompletedTask);
        var ctx = ContextFor("/api/secure");
        ctx.Request.Headers.Authorization = "Bearer super-secret-token-value";
        ctx.Request.Headers.Cookie = "session=deadbeefsecret";
        ctx.Request.Headers["X-Api-Key"] = "key-9999";

        await mw.InvokeAsync(ctx);

        Assert.NotEmpty(sink.Events);
        foreach (var evt in sink.Events)
        {
            var rendered = evt.RenderMessage();
            Assert.DoesNotContain("super-secret-token-value", rendered);
            Assert.DoesNotContain("deadbeefsecret", rendered);
            Assert.DoesNotContain("key-9999", rendered);
            Assert.DoesNotContain("Authorization", evt.Properties.Keys);
            Assert.DoesNotContain("Cookie", evt.Properties.Keys);
        }
    }

    [Fact]
    public async Task Query_string_values_are_not_logged()
    {
        var (mw, sink) = Build(_ => Task.CompletedTask);
        var ctx = ContextFor("/api/search");
        ctx.Request.QueryString = new QueryString("?token=secret123&ssn=AB123456C");

        await mw.InvokeAsync(ctx);

        Assert.NotEmpty(sink.Events);
        foreach (var evt in sink.Events)
        {
            var rendered = evt.RenderMessage();
            Assert.DoesNotContain("secret123", rendered);
            Assert.DoesNotContain("AB123456C", rendered);
        }
    }

    [Fact]
    public async Task Crlf_in_route_values_cannot_forge_enrichment_properties()
    {
        var (mw, sink) = Build(ctx =>
        {
            ctx.Request.RouteValues["companyId"] = "11111111\r\nFATAL forged";
            ctx.Request.RouteValues["employeeId"] = "22222222\nforged";
            return Task.CompletedTask;
        });

        await mw.InvokeAsync(ContextFor("/api/companies/x/employees/y"));

        var completion = sink.Events.Last();
        var companyId = Assert.Contains("CompanyId", (IReadOnlyDictionary<string, LogEventPropertyValue>)completion.Properties);
        Assert.DoesNotContain('\r', companyId.ToString());
        Assert.DoesNotContain('\n', companyId.ToString());
        AssertNoForgedLine(completion);
    }

    [Fact]
    public async Task Health_endpoints_are_not_logged()
    {
        var (mw, sink) = Build(_ => Task.CompletedTask);

        await mw.InvokeAsync(ContextFor("/health"));
        await mw.InvokeAsync(ContextFor("/alive"));

        Assert.Empty(sink.Events);
    }

    [Fact]
    public async Task Exceptions_are_still_rethrown_after_logging()
    {
        var (mw, sink) = Build(_ => throw new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => mw.InvokeAsync(ContextFor("/api/x")));
        Assert.Contains(sink.Events, e => e.Level == LogEventLevel.Error);
    }
}
