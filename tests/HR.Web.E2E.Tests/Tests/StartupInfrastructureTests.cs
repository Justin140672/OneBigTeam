using System.Net;
using System.Text;
using HR.Web.E2E.Tests.Infrastructure;

namespace HR.Web.E2E.Tests.Tests;

public sealed class SharedStartupTests
{
    private sealed class Candidate
    {
        public int DisposeCalls;
    }

    [Fact]
    public async Task Concurrent_callers_trigger_exactly_one_initialization_and_share_the_fixture()
    {
        var attempts = 0;
        var startup = new SharedStartup<Candidate>(async () =>
        {
            Interlocked.Increment(ref attempts);
            await Task.Delay(50);
            return new Candidate();
        });

        var results = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => Task.Run(() => startup.GetAsync())));

        Assert.Equal(1, attempts);
        Assert.All(results, r => Assert.Same(results[0], r));
    }

    [Fact]
    public async Task All_callers_observe_the_same_failure_and_later_callers_do_not_retry()
    {
        var attempts = 0;
        var original = new InvalidOperationException("boom");
        var startup = new SharedStartup<Candidate>(async () =>
        {
            Interlocked.Increment(ref attempts);
            await Task.Delay(20);
            throw original;
        });

        var concurrent = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
            await Assert.ThrowsAsync<InvalidOperationException>(() => startup.GetAsync())));
        var later = await Assert.ThrowsAsync<InvalidOperationException>(() => startup.GetAsync());

        Assert.Equal(1, attempts);
        Assert.All(concurrent, e => Assert.Same(original, e));
        Assert.Same(original, later);
    }

    [Fact]
    public async Task Failed_candidate_is_disposed_exactly_once_after_diagnostics_and_original_exception_is_rethrown()
    {
        var order = new List<string>();
        var candidate = new Candidate();
        var original = new InvalidOperationException("init failed");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => StartupAttempt.RunAsync(
            () => candidate,
            _ => throw original,
            (_, _) => { order.Add("diagnostics"); return Task.CompletedTask; },
            c => { order.Add("dispose"); Interlocked.Increment(ref c.DisposeCalls); return ValueTask.CompletedTask; }));

        Assert.Same(original, thrown);
        Assert.Equal(1, candidate.DisposeCalls);
        Assert.Equal(["diagnostics", "dispose"], order);
    }

    [Fact]
    public async Task Cleanup_and_diagnostic_failures_do_not_mask_the_original_exception()
    {
        var original = new InvalidOperationException("init failed");
        var log = new List<string>();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => StartupAttempt.RunAsync(
            () => new Candidate(),
            _ => throw original,
            (_, _) => throw new IOException("diagnostics failed"),
            _ => throw new IOException("dispose failed"),
            log.Add));

        Assert.Same(original, thrown);
        Assert.Equal(2, log.Count);
    }

    [Fact]
    public async Task Successful_candidate_is_not_disposed()
    {
        var candidate = new Candidate();

        var result = await StartupAttempt.RunAsync(
            () => candidate,
            _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            c => { c.DisposeCalls++; return ValueTask.CompletedTask; });

        Assert.Same(candidate, result);
        Assert.Equal(0, candidate.DisposeCalls);
    }
}

public sealed class ReadinessProbeTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static ReadinessProbe CreateProbe(
        Func<HttpRequestMessage, HttpResponseMessage> respond, List<string> files, int bodyLimit = 2048) =>
        new(
            new HttpClient(new StubHandler(respond)),
            (_, _) => { },
            (name, content) => files.Add($"{name}: {content}"),
            new ReadinessProbeOptions { BodyLimit = bodyLimit, PollDelay = TimeSpan.FromMilliseconds(1) });

    private static DateTime ShortDeadline => DateTime.UtcNow.AddMilliseconds(150);

    [Fact]
    public async Task Timeout_after_503_includes_status_and_bounded_body_of_latest_response()
    {
        var files = new List<string>();
        var probe = CreateProbe(
            r => r.RequestUri!.AbsolutePath == "/health/ready"
                ? Response(HttpStatusCode.ServiceUnavailable, """{"status":"Unhealthy","checks":[{"name":"startup-migrations"}]}""")
                : Response(HttpStatusCode.ServiceUnavailable, """{"employees":{"status":"failed"}}"""),
            files);

        var ex = await Assert.ThrowsAsync<ReadinessTimeoutException>(() =>
            probe.WaitUntilReadyAsync("http://api/health/ready", ShortDeadline, "http://api/health/startup-migrations"));

        Assert.Contains("ServiceUnavailable", ex.Message);
        Assert.Contains("startup-migrations", ex.Message);
        Assert.Contains("employees", ex.Message);
        Assert.Contains(files, f => f.StartsWith("readiness-responses.log") && f.Contains("status=503"));
        Assert.Contains(files, f => f.StartsWith("startup-migrations-responses.log"));
    }

    [Fact]
    public async Task Startup_migrations_failure_does_not_replace_the_primary_readiness_failure()
    {
        var files = new List<string>();
        var probe = CreateProbe(
            r => r.RequestUri!.AbsolutePath == "/health/ready"
                ? Response(HttpStatusCode.ServiceUnavailable, """{"status":"Unhealthy"}""")
                : throw new HttpRequestException("connection refused"),
            files);

        var ex = await Assert.ThrowsAsync<ReadinessTimeoutException>(() =>
            probe.WaitUntilReadyAsync("http://api/health/ready", ShortDeadline, "http://api/health/startup-migrations"));

        Assert.Contains("last status: ServiceUnavailable", ex.Message);
        Assert.Contains("""{"status":"Unhealthy"}""", ex.Message);
        Assert.Contains("unreachable: HttpRequestException", ex.Message);
    }

    [Fact]
    public async Task Diagnostic_bodies_are_truncated_at_the_configured_limit()
    {
        var files = new List<string>();
        var huge = new string('x', 10_000);
        var probe = CreateProbe(_ => Response(HttpStatusCode.ServiceUnavailable, huge), files, bodyLimit: 100);

        var ex = await Assert.ThrowsAsync<ReadinessTimeoutException>(() =>
            probe.WaitUntilReadyAsync("http://api/health/ready", ShortDeadline, "http://api/health/startup-migrations"));

        Assert.Contains(new string('x', 100) + "...[truncated]", ex.Message);
        Assert.DoesNotContain(new string('x', 101), ex.Message);
        Assert.All(files, f => Assert.True(f.Length < 1_000));
    }

    [Fact]
    public async Task Secrets_in_bodies_are_redacted()
    {
        var files = new List<string>();
        var probe = CreateProbe(
            _ => Response(HttpStatusCode.ServiceUnavailable, "Host=db;Password=hunter2;Username=admin"), files);

        var ex = await Assert.ThrowsAsync<ReadinessTimeoutException>(() =>
            probe.WaitUntilReadyAsync("http://api/health/ready", ShortDeadline));

        Assert.DoesNotContain("hunter2", ex.Message);
        Assert.DoesNotContain("hunter2", string.Join('\n', files));
    }

    [Fact]
    public async Task Returns_when_endpoint_becomes_healthy()
    {
        var calls = 0;
        var probe = CreateProbe(
            _ => ++calls < 3 ? Response(HttpStatusCode.ServiceUnavailable, "{}") : Response(HttpStatusCode.OK, "{}"),
            []);

        await probe.WaitUntilReadyAsync("http://api/health/ready", DateTime.UtcNow.AddSeconds(5));

        Assert.Equal(3, calls);
    }
}
