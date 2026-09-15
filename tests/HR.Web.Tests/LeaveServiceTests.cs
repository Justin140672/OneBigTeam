using System.Net;
using System.Net.Http.Json;
using HR.SharedKernel.Idempotency;
using HR.Web.Models;
using HR.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Web.Tests;

// Ticket 3 (P1) final gap: LeaveService.AdjustLeaveBalanceAsync is stateless with respect to
// operation identity (see AssetServiceTests' equivalent coverage/comments for CreateAssetAsync) -
// it sends exactly whatever key the caller supplies and returns a MutationOutcome<T> classifying
// the HTTP response into Succeeded / Rejected (definitive - safe to discard the key) or
// AmbiguousFailure (retain the key and retry). This class proves that classification, not the
// generic key-retention lifecycle (that's PendingIdempotentOperationTests).
public class LeaveServiceTests
{
    private static HrApiHttpClientFactory BuildFactory(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("hrapi", c => c.BaseAddress = new Uri("http://localhost/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return new HrApiHttpClientFactory(services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>(), new CircuitSessionState());
    }

    private static AdjustLeaveBalanceModel BuildModel() => new(
        LeaveTypeId: Guid.NewGuid(),
        AdjustmentValue: 1m,
        Reason: LeaveBalanceAdjustmentReason.Correction,
        Comments: "Integration test adjustment",
        AllowNegativeOverride: false);

    private static AdjustLeaveBalanceResponse BuildResponse() => new(
        AdjustmentId: Guid.NewGuid(),
        CompanyId: Guid.NewGuid(),
        EmployeeId: Guid.NewGuid(),
        LeaveTypeId: Guid.NewGuid(),
        LeaveBalanceId: Guid.NewGuid(),
        AdjustmentDays: 1m,
        AdjustmentHours: null,
        NewRemainingDays: 26m,
        NewRemainingHours: 195m,
        Reason: "Correction",
        Comments: "Integration test adjustment",
        AdjustedByEmployeeId: Guid.NewGuid(),
        AdjustedAt: DateTimeOffset.UtcNow);

    [Fact]
    public async Task AdjustLeaveBalanceAsync_Sends_Exactly_The_Callers_Supplied_Key()
    {
        var capturing = new CapturingHandler(new JsonResponseHandler<AdjustLeaveBalanceResponse>(BuildResponse(), HttpStatusCode.Created));
        var factory = BuildFactory(capturing);
        var service = new LeaveService(factory);
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var key = Guid.NewGuid();

        await service.AdjustLeaveBalanceAsync(companyId, employeeId, BuildModel(), key);

        Assert.Equal([key.ToString()], capturing.CapturedKeys);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task AdjustLeaveBalanceAsync_Returns_AmbiguousFailure_For_5xx_408_429_Responses(HttpStatusCode statusCode)
    {
        var capturing = new CapturingHandler(new StaticResponseHandler(statusCode));
        var factory = BuildFactory(capturing);
        var service = new LeaveService(factory);
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var key = Guid.NewGuid();

        var outcome = await service.AdjustLeaveBalanceAsync(companyId, employeeId, BuildModel(), key);

        Assert.Equal(MutationOutcomeKind.AmbiguousFailure, outcome.Kind);
        Assert.Null(outcome.Value);
        Assert.NotNull(outcome.Error);
        Assert.Equal([key.ToString()], capturing.CapturedKeys);
    }

    [Fact]
    public async Task AdjustLeaveBalanceAsync_Returns_AmbiguousFailure_On_Malformed_Success_Body()
    {
        var capturing = new CapturingHandler(new MalformedJsonResponseHandler(HttpStatusCode.Created));
        var factory = BuildFactory(capturing);
        var service = new LeaveService(factory);
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var key = Guid.NewGuid();

        var outcome = await service.AdjustLeaveBalanceAsync(companyId, employeeId, BuildModel(), key);

        Assert.Equal(MutationOutcomeKind.AmbiguousFailure, outcome.Kind);
        Assert.Null(outcome.Value);
        Assert.Equal([key.ToString()], capturing.CapturedKeys);
    }

    // Bug fix regression (P1 follow-up to Ticket 3): a 201 status code alone does not mean the body
    // was actually readable — a dropped connection or transport failure while STREAMING the success
    // body must still classify as AmbiguousFailure rather than letting an IOException escape
    // uncaught and bypass MutationOutcome entirely.
    [Fact]
    public async Task AdjustLeaveBalanceAsync_Returns_AmbiguousFailure_When_Success_Body_Stream_Throws_IOException()
    {
        var capturing = new CapturingHandler(new ThrowingBodyResponseHandler(
            HttpStatusCode.Created, () => new IOException("Connection reset while reading response body.")));
        var factory = BuildFactory(capturing);
        var service = new LeaveService(factory);
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var key = Guid.NewGuid();

        var outcome = await service.AdjustLeaveBalanceAsync(companyId, employeeId, BuildModel(), key);

        Assert.Equal(MutationOutcomeKind.AmbiguousFailure, outcome.Kind);
        Assert.Null(outcome.Value);
        Assert.Equal([key.ToString()], capturing.CapturedKeys);
    }

    // Bug fix regression: cancellation while reading (not sending) the success body must also
    // classify as AmbiguousFailure, not escape as an unhandled OperationCanceledException.
    [Fact]
    public async Task AdjustLeaveBalanceAsync_Returns_AmbiguousFailure_When_Success_Body_Read_Is_Cancelled()
    {
        var capturing = new CapturingHandler(new ThrowingBodyResponseHandler(
            HttpStatusCode.Created, () => new OperationCanceledException("Cancelled while reading response body.")));
        var factory = BuildFactory(capturing);
        var service = new LeaveService(factory);
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var key = Guid.NewGuid();

        var outcome = await service.AdjustLeaveBalanceAsync(companyId, employeeId, BuildModel(), key);

        Assert.Equal(MutationOutcomeKind.AmbiguousFailure, outcome.Kind);
        Assert.Null(outcome.Value);
        Assert.Equal([key.ToString()], capturing.CapturedKeys);
    }

    // Bug fix regression: proves the un-Complete()d key from an ambiguous body-read failure is
    // reused on the next unchanged retry — the same lifecycle AdjustLeaveBalanceDialog relies on
    // via PendingIdempotentOperation, exercised here end-to-end through the service call site
    // pattern (`(CompanyId, EmployeeId, request)`) it actually uses.
    [Fact]
    public async Task AdjustLeaveBalanceAsync_Ambiguous_Body_Read_Failure_Does_Not_Force_A_New_Key_On_Retry()
    {
        var operation = new PendingIdempotentOperation();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var request = BuildModel();

        // First attempt: server accepts (201) but the body read fails — ambiguous, so the caller
        // (mirroring AdjustLeaveBalanceDialog) must NOT call operation.Complete().
        var firstKey = operation.PrepareKey((companyId, employeeId, request));
        var capturing = new CapturingHandler(new ThrowingBodyResponseHandler(
            HttpStatusCode.Created, () => new IOException("Connection reset while reading response body.")));
        var factory = BuildFactory(capturing);
        var service = new LeaveService(factory);
        var outcome = await service.AdjustLeaveBalanceAsync(companyId, employeeId, request, firstKey);
        Assert.Equal(MutationOutcomeKind.AmbiguousFailure, outcome.Kind);

        // Retry with the exact same (unchanged) snapshot — must reuse the same key.
        var secondKey = operation.PrepareKey((companyId, employeeId, request));

        Assert.Equal(firstKey, secondKey);
    }

    [Fact]
    public async Task AdjustLeaveBalanceAsync_Returns_AmbiguousFailure_On_Network_Error_Without_Throwing()
    {
        var capturing = new CapturingHandler(new ThrowingHandler());
        var factory = BuildFactory(capturing);
        var service = new LeaveService(factory);
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var key = Guid.NewGuid();

        var outcome = await service.AdjustLeaveBalanceAsync(companyId, employeeId, BuildModel(), key);

        Assert.Equal(MutationOutcomeKind.AmbiguousFailure, outcome.Kind);
        Assert.Null(outcome.Value);
        Assert.Equal([key.ToString()], capturing.CapturedKeys);
    }

    [Fact]
    public async Task AdjustLeaveBalanceAsync_Returns_AmbiguousFailure_On_Ambient_Timeout_Without_Caller_Cancellation()
    {
        // TaskCanceledException IS an OperationCanceledException, but the caller's own token was
        // never cancelled here — this is the "client-side timeout" branch.
        var capturing = new CapturingHandler(new TimeoutThrowingHandler());
        var factory = BuildFactory(capturing);
        var service = new LeaveService(factory);
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var key = Guid.NewGuid();

        var outcome = await service.AdjustLeaveBalanceAsync(companyId, employeeId, BuildModel(), key, CancellationToken.None);

        Assert.Equal(MutationOutcomeKind.AmbiguousFailure, outcome.Kind);
        Assert.Null(outcome.Value);
        Assert.Equal([key.ToString()], capturing.CapturedKeys);
    }

    [Fact]
    public async Task AdjustLeaveBalanceAsync_Returns_AmbiguousFailure_On_Caller_Driven_Cancellation()
    {
        // Distinct branch from the ambient-timeout case above: here the CALLER's own token is
        // already cancelled when the OperationCanceledException surfaces — "cancellation requested
        // by caller after the request may already be in flight", still ambiguous rather than
        // assumed abandoned pre-dispatch.
        using var cts = new CancellationTokenSource();
        var capturing = new CapturingHandler(new CallerCancellationThrowingHandler(cts));
        var factory = BuildFactory(capturing);
        var service = new LeaveService(factory);
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var key = Guid.NewGuid();

        var outcome = await service.AdjustLeaveBalanceAsync(companyId, employeeId, BuildModel(), key, cts.Token);

        Assert.Equal(MutationOutcomeKind.AmbiguousFailure, outcome.Kind);
        Assert.Null(outcome.Value);
        Assert.Equal([key.ToString()], capturing.CapturedKeys);
    }

    [Fact]
    public async Task AdjustLeaveBalanceAsync_Returns_Succeeded_With_Value_On_201()
    {
        var response = BuildResponse();
        var capturing = new CapturingHandler(new JsonResponseHandler<AdjustLeaveBalanceResponse>(response, HttpStatusCode.Created));
        var factory = BuildFactory(capturing);
        var service = new LeaveService(factory);
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var key = Guid.NewGuid();

        var outcome = await service.AdjustLeaveBalanceAsync(companyId, employeeId, BuildModel(), key);

        Assert.Equal(MutationOutcomeKind.Succeeded, outcome.Kind);
        Assert.NotNull(outcome.Value);
        Assert.Equal(response.AdjustmentId, outcome.Value!.AdjustmentId);
        Assert.Null(outcome.Error);
        Assert.Equal([key.ToString()], capturing.CapturedKeys);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task AdjustLeaveBalanceAsync_Returns_Rejected_For_Definitive_Failure_Responses(HttpStatusCode statusCode)
    {
        var capturing = new CapturingHandler(new JsonErrorResponseHandler(statusCode, "Something went wrong."));
        var factory = BuildFactory(capturing);
        var service = new LeaveService(factory);
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var key = Guid.NewGuid();

        var outcome = await service.AdjustLeaveBalanceAsync(companyId, employeeId, BuildModel(), key);

        Assert.Equal(MutationOutcomeKind.Rejected, outcome.Kind);
        Assert.Null(outcome.Value);
        Assert.NotNull(outcome.Error);
        Assert.Equal([key.ToString()], capturing.CapturedKeys);
    }

    [Fact]
    public async Task AdjustLeaveBalanceAsync_Returns_Rejected_With_First_Validation_Message_On_422()
    {
        var capturing = new CapturingHandler(new ValidationErrorResponseHandler(
            new Dictionary<string, string[]>
            {
                ["adjustmentValue"] = ["Adjustment value must not be zero."],
                ["comments"] = ["Comments are required."],
            }));
        var factory = BuildFactory(capturing);
        var service = new LeaveService(factory);
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var key = Guid.NewGuid();

        var outcome = await service.AdjustLeaveBalanceAsync(companyId, employeeId, BuildModel(), key);

        Assert.Equal(MutationOutcomeKind.Rejected, outcome.Kind);
        Assert.Equal("Adjustment value must not be zero.", outcome.Error);
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

    private sealed class JsonResponseHandler<T>(T payload, HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode) { Content = JsonContent.Create(payload) };
            return Task.FromResult(response);
        }
    }

    private sealed class StaticResponseHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Network failure");
    }

    /// <summary>Simulates a client-side request timeout: throws TaskCanceledException (an
    /// OperationCanceledException) without the caller's own CancellationToken ever being
    /// cancelled — the "ambient timeout" branch.</summary>
    private sealed class TimeoutThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new TaskCanceledException("The request timed out.");
    }

    /// <summary>Simulates cancellation driven by the caller: cancels the supplied
    /// CancellationTokenSource itself and then throws using that (now-cancelled) token, so
    /// cancellationToken.IsCancellationRequested is true when the service's catch clause runs —
    /// the "caller cancelled" branch, distinct from the ambient-timeout branch above.</summary>
    private sealed class CallerCancellationThrowingHandler(CancellationTokenSource cts) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cts.Cancel();
            throw new OperationCanceledException("Cancelled by caller.", cts.Token);
        }
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
                Content = JsonContent.Create(new { error = errorMessage })
            };
            return Task.FromResult(response);
        }
    }

    private sealed class ValidationErrorResponseHandler(Dictionary<string, string[]> errors) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = JsonContent.Create(new { errors })
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
