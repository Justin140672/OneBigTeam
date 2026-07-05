using System.Net;
using System.Net.Http.Json;
using HR.Web.Models;
using HR.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Web.Tests;

public class CompensationServiceTests
{
    private static IHttpClientFactory BuildFactory(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("hrapi", c => c.BaseAddress = new Uri("http://localhost/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    }

    [Fact]
    public async Task GetCurrentCompensationAsync_Returns_Model_When_Api_Returns_Ok()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var response = new CurrentCompensationModel(
            Guid.NewGuid(), companyId, employeeId, new DateOnly(2026, 1, 1), null,
            "Annual", 45000m, 45000m, "GBP", 37.5m, 1m, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new CompensationService(factory);

        var result = await service.GetCurrentCompensationAsync(companyId, employeeId);

        Assert.NotNull(result);
        Assert.Equal(45000m, result.Salary);
        Assert.Equal(45000m, result.AnnualisedSalary);
        Assert.Equal("Annual", result.SalaryType);
    }

    [Fact]
    public async Task GetCurrentCompensationAsync_Returns_Null_When_Api_Returns_NotFound()
    {
        var factory = BuildFactory(new StaticResponseHandler(HttpStatusCode.NotFound));
        var service = new CompensationService(factory);

        var result = await service.GetCurrentCompensationAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCurrentCompensationAsync_Returns_Null_On_Network_Failure()
    {
        var factory = BuildFactory(new ThrowingHandler());
        var service = new CompensationService(factory);

        var result = await service.GetCurrentCompensationAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCompensationHistoryAsync_Returns_Items_When_Api_Returns_Ok()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var response = new GetCompensationHistoryResponse(
        [
            new CompensationHistoryItemModel(Guid.NewGuid(), new DateOnly(2023, 1, 1), null, "Annual", 145000m, "GBP", 37.5m, 1m, "Promoted to CTO", DateTimeOffset.UtcNow),
            new CompensationHistoryItemModel(Guid.NewGuid(), new DateOnly(2020, 1, 6), new DateOnly(2022, 12, 31), "Annual", 120000m, "GBP", 37.5m, 1m, "Starting salary", DateTimeOffset.UtcNow)
        ]);

        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new CompensationService(factory);

        var result = await service.GetCompensationHistoryAsync(companyId, employeeId);

        Assert.Equal(2, result.Count);
        Assert.Equal(145000m, result[0].Salary);
        Assert.Null(result[0].EffectiveTo);
        Assert.Equal(new DateOnly(2022, 12, 31), result[1].EffectiveTo);
    }

    [Fact]
    public async Task GetCompensationHistoryAsync_Returns_Empty_List_On_Network_Failure()
    {
        var factory = BuildFactory(new ThrowingHandler());
        var service = new CompensationService(factory);

        var result = await service.GetCompensationHistoryAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Empty(result);
    }

    // ── Fake handlers ────────────────────────────────────────────────────────────

    private sealed class JsonResponseHandler(HttpStatusCode statusCode, object payload) : HttpMessageHandler
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
}
