using System.Net;
using System.Net.Http.Json;
using HR.Web.Models;
using HR.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Web.Tests;

public class AuditHistoryServiceTests
{
    private static IHttpClientFactory BuildFactory(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("hrapi", c => c.BaseAddress = new Uri("http://localhost/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    }

    [Fact]
    public async Task GetEmployeeAuditHistoryAsync_Returns_Items_When_Api_Returns_Ok()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var response = new GetEmployeeAuditHistoryResponse(
        [
            new AuditHistoryItemModel(DateTimeOffset.UtcNow, "Compensation record created", "Employees", "Alice Smith",
                [new AuditFieldChangeModel("Effective From", "—", "2027-01-01")]),
            new AuditHistoryItemModel(DateTimeOffset.UtcNow.AddDays(-1), "Leave request approved", "Leave", "Bob Jones", [])
        ]);

        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new AuditHistoryService(factory);

        var result = await service.GetEmployeeAuditHistoryAsync(companyId, employeeId);

        Assert.Equal(2, result.Count);
        Assert.Equal("Compensation record created", result[0].Action);
        Assert.Equal("Employees", result[0].Module);
        Assert.Equal("Alice Smith", result[0].User);

        var change = Assert.Single(result[0].Changes);
        Assert.Equal("Effective From", change.Field);
        Assert.Equal("—", change.Before);
        Assert.Equal("2027-01-01", change.After);

        Assert.Empty(result[1].Changes);
    }

    [Fact]
    public async Task GetEmployeeAuditHistoryAsync_Returns_Empty_List_On_Network_Failure()
    {
        var factory = BuildFactory(new ThrowingHandler());
        var service = new AuditHistoryService(factory);

        var result = await service.GetEmployeeAuditHistoryAsync(Guid.NewGuid(), Guid.NewGuid());

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

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Network failure");
    }
}
