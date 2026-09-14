using System.Net;
using System.Net.Http.Json;
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
    public async Task CreateAssetAsync_Propagates_Network_Failure_Without_Swallowing_It()
    {
        // The caller (PendingIdempotentOperation's owner) relies on this to tell "ambiguous, keep
        // the key" (an exception) apart from a definitive outcome (a returned tuple).
        var capturing = new CapturingHandler(new ThrowingHandler());
        var factory = BuildFactory(capturing);
        var service = new AssetService(factory);
        var companyId = Guid.NewGuid();
        var request = new CreateAssetRequest(companyId, "A-001", Guid.NewGuid(), "Laptop", null, null, null, null, null);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.CreateAssetAsync(companyId, request, Guid.NewGuid()));
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
}
