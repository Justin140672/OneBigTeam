using System.Net;
using System.Net.Http.Json;
using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>ADM-03: PUT .../administrative-alerts/{alertId}/acknowledge.</summary>
[Collection("Integration")]
public class AcknowledgeAdministrativeAlertEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public AcknowledgeAdministrativeAlertEndpointTests(ApiWebApplicationFactory factory) => _factory = factory;

    private static string Url(Guid companyId, Guid alertId) =>
        $"/api/companies/{companyId}/administrative-alerts/{alertId}/acknowledge";

    private async Task<HttpClient> ClientFor(Guid companyId, Guid userId, Guid roleId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, roleId, companyId);
        return client;
    }

    private async Task<Guid> SeedAlertAsync(Guid companyId, bool acknowledged = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var alert = AdministrativeAlert.Raise(Guid.NewGuid(), new RaiseAdministrativeAlertCommand(
            companyId, AdministrativeAlertSeverity.Warning, AdministrativeAlertCategory.Security,
            "s", "d", Now, $"k:{Guid.NewGuid():N}", null, null, null, null), Now);
        if (acknowledged) alert.Acknowledge(Guid.NewGuid(), Now);
        db.AdministrativeAlerts.Add(alert);
        await db.SaveChangesAsync();
        return alert.Id;
    }

    [Fact]
    public async Task Returns_Unauthorized_For_Anonymous()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(Url(Guid.NewGuid(), Guid.NewGuid()), new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_For_Manager()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.Manager);

        var response = await client.PutAsJsonAsync(Url(companyId, Guid.NewGuid()), new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_NoContent_On_Success()
    {
        var companyId = Guid.NewGuid();
        var alertId = await SeedAlertAsync(companyId);
        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);

        var response = await client.PutAsJsonAsync(Url(companyId, alertId), new { });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Returns_NotFound_For_Unknown_Alert()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);

        var response = await client.PutAsJsonAsync(Url(companyId, Guid.NewGuid()), new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Conflict_When_Already_Acknowledged()
    {
        var companyId = Guid.NewGuid();
        var alertId = await SeedAlertAsync(companyId, acknowledged: true);
        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);

        var response = await client.PutAsJsonAsync(Url(companyId, alertId), new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
