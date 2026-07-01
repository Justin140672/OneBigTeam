using System.Net;
using System.Net.Http.Json;
using HR.Web.Models;
using HR.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Web.Tests;

public class AssetServiceTests
{
    private static IHttpClientFactory BuildFactory(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("hrapi", c => c.BaseAddress = new Uri("http://localhost/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
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

    // ── Fake handlers ────────────────────────────────────────────────────────────

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
