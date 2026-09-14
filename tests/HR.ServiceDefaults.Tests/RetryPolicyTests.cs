using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HR.ServiceDefaults.Tests;

/// <summary>
/// Ticket 3 (P1) follow-up. Exercises the real <c>AddStandardResilienceHandler</c> pipeline
/// configured by <c>AddServiceDefaults</c> - not a standalone predicate unit test - so a regression
/// in how the resilience handler, <c>RequestMethodCapturingHandler</c>, and the retry predicate
/// interact together is actually caught.
/// </summary>
public class RetryPolicyTests
{
    private static HttpClient BuildClient(FakeHandler inner)
    {
        var builder = Host.CreateApplicationBuilder();

        // Ticket 3 (P1) follow-up item 6: on Windows, the generic host's default logging can
        // include the EventLog provider, which throws under a non-administrator test-runner
        // identity the moment Polly/OpenTelemetry emits a log record through it. These tests only
        // care about HTTP retry behaviour, not log output, so clear the default providers and use
        // an in-memory sink that works identically on Windows and Linux CI without elevation.
        builder.Logging.ClearProviders();

        builder.AddServiceDefaults();
        builder.Services.AddHttpClient("under-test").ConfigurePrimaryHttpMessageHandler(() => inner);

        var host = builder.Build();
        return host.Services.GetRequiredService<IHttpClientFactory>().CreateClient("under-test");
    }

    [Fact]
    public async Task Get_Returning_500_Then_200_Is_Sent_Twice()
    {
        var inner = FakeHandler.RespondWith(HttpStatusCode.InternalServerError, HttpStatusCode.OK);
        var client = BuildClient(inner);

        var response = await client.GetAsync("http://fake-host/resource");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task Get_Timing_Out_Before_Any_Response_Is_Retried()
    {
        // First attempt "times out" (throws, producing no HttpResponseMessage - Outcome.Result is
        // null); this is exactly the case the RequestMethodCapturingHandler fallback exists for.
        var inner = FakeHandler.ThrowThenRespond(() => new Polly.Timeout.TimeoutRejectedException(), HttpStatusCode.OK);
        var client = BuildClient(inner);

        var response = await client.GetAsync("http://fake-host/resource");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task Post_Returning_500_Is_Sent_Once()
    {
        var inner = FakeHandler.RespondWith(HttpStatusCode.InternalServerError, HttpStatusCode.OK);
        var client = BuildClient(inner);

        var response = await client.PostAsync("http://fake-host/resource", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task Post_Timing_Out_After_Simulated_Commit_Is_Sent_Once()
    {
        // Simulates the exact ticket-3 scenario: the handler committed its write, but the response
        // never made it back (connection dropped / attempt timed out) - Outcome.Result is null.
        var inner = FakeHandler.ThrowThenRespond(() => new Polly.Timeout.TimeoutRejectedException(), HttpStatusCode.OK);
        var client = BuildClient(inner);

        await Assert.ThrowsAnyAsync<Exception>(
            () => client.PostAsync("http://fake-host/resource", new StringContent("{}")));

        Assert.Equal(1, inner.CallCount);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Unsafe_Non_Post_Methods_Are_Not_Retried_On_500(string method)
    {
        var inner = FakeHandler.RespondWith(HttpStatusCode.InternalServerError, HttpStatusCode.OK);
        var client = BuildClient(inner);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), "http://fake-host/resource"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task Head_Returning_500_Then_200_Is_Retried()
    {
        var inner = FakeHandler.RespondWith(HttpStatusCode.InternalServerError, HttpStatusCode.OK);
        var client = BuildClient(inner);

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "http://fake-host/resource"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task Options_Returning_500_Then_200_Is_Retried()
    {
        var inner = FakeHandler.RespondWith(HttpStatusCode.InternalServerError, HttpStatusCode.OK);
        var client = BuildClient(inner);

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "http://fake-host/resource"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.CallCount);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Unsafe_Non_Post_Methods_Are_Sent_Once_After_Timeout(string method)
    {
        var inner = FakeHandler.ThrowThenRespond(() => new Polly.Timeout.TimeoutRejectedException(), HttpStatusCode.OK);
        var client = BuildClient(inner);

        await Assert.ThrowsAnyAsync<Exception>(
            () => client.SendAsync(new HttpRequestMessage(new HttpMethod(method), "http://fake-host/resource")));

        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task Caller_Cancellation_Does_Not_Trigger_Retry()
    {
        // Distinguishes an attempt timeout (retried, tested above) from the CALLER cancelling the
        // request outright - the latter must never be treated as a transient failure worth retrying.
        var inner = FakeHandler.RespondWith(HttpStatusCode.InternalServerError, HttpStatusCode.OK);
        var client = BuildClient(inner);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetAsync("http://fake-host/resource", cts.Token));

        Assert.Equal(0, inner.CallCount);
    }

    [Fact]
    public async Task Get_Retries_Are_Bounded_Not_Indefinite()
    {
        // Persistent 5xx on a safe method must still stop retrying eventually (MaxRetryAttempts = 4
        // in HR.ServiceDefaults, so 1 initial attempt + 4 retries = 5 total) rather than retrying
        // forever.
        var inner = FakeHandler.AlwaysRespondWith(HttpStatusCode.InternalServerError);
        var client = BuildClient(inner);

        var response = await client.GetAsync("http://fake-host/resource");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(5, inner.CallCount);
    }

    private sealed class FakeHandler : DelegatingHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new();
        public int CallCount { get; private set; }

        public static FakeHandler RespondWith(params HttpStatusCode[] statusCodes)
        {
            var handler = new FakeHandler();
            foreach (var code in statusCodes)
                handler._responses.Enqueue(() => new HttpResponseMessage(code));
            return handler;
        }

        public static FakeHandler ThrowThenRespond(Func<Exception> exceptionFactory, HttpStatusCode thenRespondWith)
        {
            var handler = new FakeHandler();
            handler._responses.Enqueue(() => throw exceptionFactory());
            handler._responses.Enqueue(() => new HttpResponseMessage(thenRespondWith));
            return handler;
        }

        public static FakeHandler AlwaysRespondWith(HttpStatusCode statusCode)
        {
            var handler = new FakeHandler { _alwaysRespond = () => new HttpResponseMessage(statusCode) };
            return handler;
        }

        private Func<HttpResponseMessage>? _alwaysRespond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Respect caller cancellation like a real HttpMessageHandler would - a pre-cancelled
            // token must never even count as an attempt.
            cancellationToken.ThrowIfCancellationRequested();

            CallCount++;
            if (_alwaysRespond is { } always)
                return Task.FromResult(always());

            var next = _responses.Count > 0 ? _responses.Dequeue() : _responses.Peek();
            return Task.FromResult(next());
        }
    }
}
