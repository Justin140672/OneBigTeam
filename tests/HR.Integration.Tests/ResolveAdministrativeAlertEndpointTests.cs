using System.Net;
using System.Net.Http.Json;
using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>ADM-03: PUT .../administrative-alerts/{alertId}/resolve.</summary>
[Collection("Integration")]
public class ResolveAdministrativeAlertEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public ResolveAdministrativeAlertEndpointTests(ApiWebApplicationFactory factory) => _factory = factory;

    private static string Url(Guid companyId, Guid alertId) =>
        $"/api/companies/{companyId}/administrative-alerts/{alertId}/resolve";

    private async Task<HttpClient> ClientFor(Guid companyId, Guid userId, Guid roleId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, roleId, companyId);
        return client;
    }

    private async Task<Guid> SeedAlertAsync(Guid companyId, bool resolved = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var alert = AdministrativeAlert.Raise(Guid.NewGuid(), new RaiseAdministrativeAlertCommand(
            companyId, AdministrativeAlertSeverity.Warning, AdministrativeAlertCategory.Security,
            "s", "d", Now, $"k:{Guid.NewGuid():N}", null, null, null, null), Now);
        if (resolved) alert.Resolve(Guid.NewGuid(), null, Now);
        db.AdministrativeAlerts.Add(alert);
        await db.SaveChangesAsync();
        return alert.Id;
    }

    [Fact]
    public async Task Returns_Unauthorized_For_Anonymous()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(Url(Guid.NewGuid(), Guid.NewGuid()), new { resolutionNote = "x" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_For_CompanyAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.CompanyAdministrator);

        var response = await client.PutAsJsonAsync(Url(companyId, Guid.NewGuid()), new { resolutionNote = "x" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_NoContent_On_Success()
    {
        var companyId = Guid.NewGuid();
        var alertId = await SeedAlertAsync(companyId);
        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);

        var response = await client.PutAsJsonAsync(Url(companyId, alertId), new { resolutionNote = "Investigated and cleared." });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Conflict_When_Already_Resolved()
    {
        var companyId = Guid.NewGuid();
        var alertId = await SeedAlertAsync(companyId, resolved: true);
        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);

        var response = await client.PutAsJsonAsync(Url(companyId, alertId), new { resolutionNote = (string?)null });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Returns_UnprocessableEntity_When_Note_Too_Long()
    {
        var companyId = Guid.NewGuid();
        var alertId = await SeedAlertAsync(companyId);
        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);

        var response = await client.PutAsJsonAsync(Url(companyId, alertId), new { resolutionNote = new string('x', 1001) });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
