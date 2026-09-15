using System.Net;
using System.Net.Http.Json;
using HR.SharedKernel.Idempotency;
using HR.Web.Models;
using HR.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Web.Tests;

public class AssetServiceTests
{
    private static HrApiHttpClientFactory BuildFactory(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("hrapi", c => c.BaseAddress = new Uri("http://localhost/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return new HrApiHttpClientFactory(services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>(), new CircuitSessionState());
    }

    [Fact]
    public async Task GetEmployeeAssignmentsAsync_Returns_Items_When_Api_Returns_Ok()
    {
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var assignedBy = Guid.NewGuid();
        var assignedAt = DateTimeOffset.UtcNow;

        var items = new List<EmployeeAssetItem>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), employeeId, assignedBy, assignedAt,
                null, "A001", "Laptop", "Dell", "XPS 15", "SN-001", "IT Equipment", true)
        };

        var factory = BuildFactory(new JsonListResponseHandler<EmployeeAssetItem>(items));
        var service = new AssetService(factory);

        var result = await service.GetEmployeeAssignmentsAsync(companyId, employeeId);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("A001", result[0].AssetNumber);
        Assert.Equal("Laptop", result[0].Name);
        Assert.True(result[0].IsAcknowledged);
    }

    [Fact]
    public async Task GetEmployeeAssignmentsAsync_Returns_Null_When_Api_Returns_Error()
    {
        var factory = BuildFactory(new StaticResponseHandler(HttpStatusCode.InternalServerError));
        var service = new AssetService(factory);

        var result = await service.GetEmployeeAssignmentsAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetEmployeeAssignmentsAsync_Returns_Null_When_Network_Fails()
    {
        var factory = BuildFactory(new ThrowingHandler());
        var service = new AssetService(factory);

        var result = await service.GetEmployeeAssignmentsAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetEmployeeAssignmentsAsync_Returns_Empty_List_When_Api_Returns_Empty()
    {
        var factory = BuildFactory(new JsonListResponseHandler<EmployeeAssetItem>([]));
        var service = new AssetService(factory);

        var result = await service.GetEmployeeAssignmentsAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    // Ticket 3 (P1) final follow-up item 1: AssetService.CreateAssetAsync is stateless with
    // respect to operation identity now - it sends exactly whatever key the caller supplies,
    // and owns none itself. The "reuse on retry / rotate after a material change / discard on a
    // definitive outcome" lifecycle lives in the caller (PendingIdempotentOperationTests covers
    // that directly) and in EditPageBase (the real UI caller for asset creation).
    [Fact]
    public async Task CreateAssetAsync_Sends_Exactly_The_Callers_Supplied_Key()
    {
        var response = new CreateAssetResponse(
            Guid.NewGuid(), Guid.NewGuid(), "A-001", Guid.NewGuid(), "Laptop", null, null, null, null, null,
            "Available", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var capturing = new CapturingHandler(new JsonResponseHandler<CreateAssetResponse>(response));
        var factory = BuildFactory(capturing);
        var service = new AssetService(factory);
        var companyId = Guid.NewGuid();
        var request = new CreateAssetRequest(companyId, "A-001", Guid.NewGuid(), "Laptop", null, null, null, null, null);

        var keyOne = Guid.NewGuid();
        var keyTwo = Guid.NewGuid();
        await service.CreateAssetAsync(companyId, request, keyOne);
        await service.CreateAssetAsync(companyId, request, keyTwo);

        Assert.Equal([keyOne.ToString(), keyTwo.ToString()], capturing.CapturedKeys);
    }

    [Fact]
    public async Task CreateAssetAsync_Returns_AmbiguousFailure_On_Network_Error_Without_Throwing()
    {
        // Ticket 3 (P1) final gap: the caller relies on a returned MutationOutcome (not a thrown
        // exception) to tell "ambiguous, keep the key" apart from a definitive outcome - see
        // MutationOutcomeKind for the classification rule.
        var capturing = new CapturingHandler(new ThrowingHandler());
        var factory = BuildFactory(capturing);
        var service = new AssetService(factory);
        var companyId = Guid.NewGuid();
        var request = new CreateAssetRequest(companyId, "A-001", Guid.NewGuid(), "Laptop", null, null, null, null, null);

        var outcome = await service.CreateAssetAsync(companyId, request, Guid.NewGuid());

        Assert.Equal(HR.SharedKernel.Idempotency.MutationOutcomeKind.AmbiguousFailure, outcome.Kind);
        Assert.Null(outcome.Value);
    }

    // ── Ticket 3 (P1) final gap: MutationOutcome classification matrix ────────────

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task CreateAssetAsync_Returns_AmbiguousFailure_For_5xx_408_429_Responses(HttpStatusCode statusCode)
    {
        var capturing = new CapturingHandler(new StaticResponseHandler(statusCode));
        var factory = BuildFactory(capturing);
        var service = new AssetService(factory);
        var companyId = Guid.NewGuid();
        var request = new CreateAssetRequest(companyId, "A-001", Guid.NewGuid(), "Laptop", null, null, null, null, null);
        var key = Guid.NewGuid();

        var outcome = await service.CreateAssetAsync(companyId, request, key);

        Assert.Equal(HR.SharedKernel.Idempotency.MutationOutcomeKind.AmbiguousFailure, outcome.Kind);
        Assert.Null(outcome.Value);
        Assert.NotNull(outcome.Error);
        Assert.Equal([key.ToString()], capturing.CapturedKeys);
    }

    [Fact]
    public async Task CreateAssetAsync_Returns_AmbiguousFailure_On_Malformed_Success_Body()
    {
        var capturing = new CapturingHandler(new MalformedJsonResponseHandler(HttpStatusCode.Created));
        var factory = BuildFactory(capturing);
        var service = new AssetService(factory);
        var companyId = Guid.NewGuid();
        var request = new CreateAssetRequest(companyId, "A-001", Guid.NewGuid(), "Laptop", null, null, null, null, null);
        var key = Guid.NewGuid();

        var outcome = await service.CreateAssetAsync(companyId, request, key);

        Assert.Equal(HR.SharedKernel.Idempotency.MutationOutcomeKind.AmbiguousFailure, outcome.Kind);
        Assert.Null(outcome.Value);
        Assert.Equal([key.ToString()], capturing.CapturedKeys);
    }

    // Bug fix regression (P1 follow-up to Ticket 3): a 201/200 status code alone does not mean the
    // body was actually readable — a dropped connection or transport failure while STREAMING the
    // success body must still classify as AmbiguousFailure rather than letting an IOException
    // escape uncaught and bypass MutationOutcome entirely.
    [Fact]
    public async Task CreateAssetAsync_Returns_AmbiguousFailure_When_Success_Body_Stream_Throws_IOException()
    {
        var capturing = new CapturingHandler(new ThrowingBodyResponseHandler(
            HttpStatusCode.Created, () => new IOException("Connection reset while reading response body.")));
        var factory = BuildFactory(capturing);
        var service = new AssetService(factory);
        var companyId = Guid.NewGuid();
        var request = new CreateAssetRequest(companyId, "A-001", Guid.NewGuid(), "Laptop", null, null, null, null, null);
        var key = Guid.NewGuid();

        var outcome = await service.CreateAssetAsync(companyId, request, key);

        Assert.Equal(HR.SharedKernel.Idempotency.MutationOutcomeKind.AmbiguousFailure, outcome.Kind);
        Assert.Null(outcome.Value);
        Assert.Equal([key.ToString()], capturing.CapturedKeys);
    }

    // Bug fix regression: cancellation while reading (not sending) the success body must also
    // classify as AmbiguousFailure, not escape as an unhandled OperationCanceledException.
    [Fact]
    public async Task CreateAssetAsync_Returns_AmbiguousFailure_When_Success_Body_Read_Is_Cancelled()
    {
        var capturing = new CapturingHandler(new ThrowingBodyResponseHandler(
            HttpStatusCode.Created, () => new OperationCanceledException("Cancelled while reading response body.")));
        var factory = BuildFactory(capturing);
        var service = new AssetService(factory);
        var companyId = Guid.NewGuid();
        var request = new CreateAssetRequest(companyId, "A-001", Guid.NewGuid(), "Laptop", null, null, null, null, null);
        var key = Guid.NewGuid();

        var outcome = await service.CreateAssetAsync(companyId, request, key);

        Assert.Equal(HR.SharedKernel.Idempotency.MutationOutcomeKind.AmbiguousFailure, outcome.Kind);
        Assert.Null(outcome.Value);
        Assert.Equal([key.ToString()], capturing.CapturedKeys);
    }

    // Bug fix regression: proves the un-Complete()d key from an ambiguous body-read failure is
    // reused on the next unchanged retry — the same lifecycle EditPageBase relies on via
    // PendingIdempotentOperation, exercised here end-to-end through the service call site pattern.
    [Fact]
    public async Task CreateAssetAsync_Ambiguous_Body_Read_Failure_Does_Not_Force_A_New_Key_On_Retry()
    {
        var operation = new PendingIdempotentOperation();
        var companyId = Guid.NewGuid();
        var request = new CreateAssetRequest(companyId, "A-001", Guid.NewGuid(), "Laptop", null, null, null, null, null);

        // First attempt: server accepts (201) but the body read fails — ambiguous, so the caller
        // (mirroring EditPageBase) must NOT call operation.Complete().
        var firstKey = operation.PrepareKey((companyId, request));
        var capturing = new CapturingHandler(new ThrowingBodyResponseHandler(
            HttpStatusCode.Created, () => new IOException("Connection reset while reading response body.")));
        var factory = BuildFactory(capturing);
        var service = new AssetService(factory);
        var outcome = await service.CreateAssetAsync(companyId, request, firstKey);
        Assert.Equal(HR.SharedKernel.Idempotency.MutationOutcomeKind.AmbiguousFailure, outcome.Kind);

        // Retry with the exact same (unchanged) snapshot — must reuse the same key.
        var secondKey = operation.PrepareKey((companyId, request));

        Assert.Equal(firstKey, secondKey);
    }

    [Fact]
    public async Task CreateAssetAsync_Returns_AmbiguousFailure_On_Ambient_Timeout_Without_Caller_Cancellation()
    {
        // TaskCanceledException IS an OperationCanceledException, but the caller's own token was
        // never cancelled here — this is the "client-side timeout" branch, not the "caller
        // cancelled" branch.
        var capturing = new CapturingHandler(new TimeoutThrowingHandler());
        var factory = BuildFactory(capturing);
        var service = new AssetService(factory);
        var companyId = Guid.NewGuid();
        var request = new CreateAssetRequest(companyId, "A-001", Guid.NewGuid(), "Laptop", null, null, null, null, null);
        var key = Guid.NewGuid();

        var outcome = await service.CreateAssetAsync(companyId, request, key, CancellationToken.None);

        Assert.Equal(HR.SharedKernel.Idempotency.MutationOutcomeKind.AmbiguousFailure, outcome.Kind);
        Assert.Null(outcome.Value);
        Assert.Equal([key.ToString()], capturing.CapturedKeys);
    }

    [Fact]
    public async Task CreateAssetAsync_Returns_Succeeded_With_Value_On_201()
    {
        var response = new CreateAssetResponse(
            Guid.NewGuid(), Guid.NewGuid(), "A-001", Guid.NewGuid(), "Laptop", null, null, null, null, null,
            "Available", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var capturing = new CapturingHandler(new JsonResponseHandler<CreateAssetResponse>(response));
        var factory = BuildFactory(capturing);
        var service = new AssetService(factory);
        var companyId = Guid.NewGuid();
        var request = new CreateAssetRequest(companyId, "A-001", Guid.NewGuid(), "Laptop", null, null, null, null, null);
        var key = Guid.NewGuid();

        var outcome = await service.CreateAssetAsync(companyId, request, key);

        Assert.Equal(HR.SharedKernel.Idempotency.MutationOutcomeKind.Succeeded, outcome.Kind);
        Assert.NotNull(outcome.Value);
        Assert.Equal(response.Id, outcome.Value!.Id);
        Assert.Null(outcome.Error);
        Assert.Equal([key.ToString()], capturing.CapturedKeys);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task CreateAssetAsync_Returns_Rejected_For_Definitive_Failure_Responses(HttpStatusCode statusCode)
    {
        var capturing = new CapturingHandler(new JsonErrorResponseHandler(statusCode, "Something went wrong."));
        var factory = BuildFactory(capturing);
        var service = new AssetService(factory);
        var companyId = Guid.NewGuid();
        var request = new CreateAssetRequest(companyId, "A-001", Guid.NewGuid(), "Laptop", null, null, null, null, null);
        var key = Guid.NewGuid();

        var outcome = await service.CreateAssetAsync(companyId, request, key);

        Assert.Equal(HR.SharedKernel.Idempotency.MutationOutcomeKind.Rejected, outcome.Kind);
        Assert.Null(outcome.Value);
        Assert.NotNull(outcome.Error);
        Assert.Equal([key.ToString()], capturing.CapturedKeys);
    }

    [Fact]
    public async Task CreateAssetAsync_Returns_Rejected_With_First_Validation_Message_On_422()
    {
        var capturing = new CapturingHandler(new JsonErrorResponseHandler(
            HttpStatusCode.UnprocessableEntity, "Asset number is required."));
        var factory = BuildFactory(capturing);
        var service = new AssetService(factory);
        var companyId = Guid.NewGuid();
        var request = new CreateAssetRequest(companyId, "A-001", Guid.NewGuid(), "Laptop", null, null, null, null, null);
        var key = Guid.NewGuid();

        var outcome = await service.CreateAssetAsync(companyId, request, key);

        Assert.Equal(HR.SharedKernel.Idempotency.MutationOutcomeKind.Rejected, outcome.Kind);
        Assert.Equal("Asset number is required.", outcome.Error);
        Assert.Equal([key.ToString()], capturing.CapturedKeys);
    }

    // ── Fake handlers ────────────────────────────────────────────────────────────

    private sealed class CapturingHandler(HttpMessageHandler inner) : HttpMessageHandler
    {
        public HttpMessageHandler Inner { get; set; } = inner;
        public List<string> CapturedKeys { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.TryGetValues("Idempotency-Key", out var values))
                CapturedKeys.Add(values.Single());

            var invoker = new HttpMessageInvoker(Inner);
            return await invoker.SendAsync(request, cancellationToken);
        }
    }

    private sealed class JsonResponseHandler<T>(T payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(payload) };
            return Task.FromResult(response);
        }
    }

    private sealed class StaticResponseHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }

    private sealed class JsonListResponseHandler<T>(List<T> payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = JsonContent.Create(payload);
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Network failure");
    }

    /// <summary>Simulates a client-side request timeout: throws TaskCanceledException (an
    /// OperationCanceledException) without the caller's own CancellationToken ever being
    /// cancelled — the "ambient timeout" branch, distinct from caller-driven cancellation.</summary>
    private sealed class TimeoutThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new TaskCanceledException("The request timed out.");
    }

    private sealed class MalformedJsonResponseHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("{ this is not valid json", System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class JsonErrorResponseHandler(HttpStatusCode statusCode, string errorMessage) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = JsonContent.Create(new { error = errorMessage, errors = new Dictionary<string, string[]> { ["assetNumber"] = [errorMessage] } })
            };
            return Task.FromResult(response);
        }
    }

    /// <summary>Returns a response with a successful status code but whose content stream throws
    /// when actually read (as opposed to when the request is sent) — simulates a dropped connection
    /// or cancellation while streaming the response body.</summary>
    private sealed class ThrowingBodyResponseHandler(HttpStatusCode statusCode, Func<Exception> exceptionFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode) { Content = new ThrowingContent(exceptionFactory) };
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingContent(Func<Exception> exceptionFactory) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw exceptionFactory();

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            throw exceptionFactory();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
