using System.Net;
using System.Net.Http.Json;
using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// ADM-03: GET .../companies/{companyId}/administrative-alerts/unread-count. Gated by
/// <c>admin-alerts:view</c> (HR Administrator). The count is company-scoped and excludes both
/// read and resolved alerts.
///
/// This is a committed feature delivered alongside held NFR-07 work; the retention / legal-hold /
/// PurgeExpiredReadNotificationsJob source is deliberately not exercised here.
/// </summary>
[Collection("Integration")]
public class GetAdministrativeAlertUnreadCountEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public GetAdministrativeAlertUnreadCountEndpointTests(ApiWebApplicationFactory factory) => _factory = factory;

    private static string Url(Guid companyId) => $"/api/companies/{companyId}/administrative-alerts/unread-count";

    private async Task<HttpClient> ClientFor(Guid companyId, Guid userId, Guid roleId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, roleId, companyId);
        return client;
    }

    private async Task<Guid> SeedAlertAsync(Guid companyId, bool read = false, bool resolved = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var alert = AdministrativeAlert.Raise(Guid.NewGuid(), new RaiseAdministrativeAlertCommand(
            companyId, AdministrativeAlertSeverity.Warning, AdministrativeAlertCategory.IntegrationDelivery,
            "Delivery failing", "detail", Now, $"k:{Guid.NewGuid():N}", null, null, null, null), Now);
        if (read) alert.MarkAsRead();
        if (resolved) alert.Resolve(Guid.NewGuid(), null, Now);
        db.AdministrativeAlerts.Add(alert);
        await db.SaveChangesAsync();
        return alert.Id;
    }

    [Fact]
    public async Task Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(Url(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public static IEnumerable<object[]> ForbiddenRoles() => new[]
    {
        new object[] { SystemRoles.Employee },
        new object[] { SystemRoles.Manager },
        new object[] { SystemRoles.CompanyAdministrator },
    };

    [Theory]
    [MemberData(nameof(ForbiddenRoles))]
    public async Task Returns_Forbidden_For_NonHrAdministrator_Roles(Guid roleId)
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, Guid.NewGuid(), roleId);

        var response = await client.GetAsync(Url(companyId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Zero_When_No_Alerts()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);

        var payload = await client.GetFromJsonAsync<CountPayload>(Url(companyId));

        Assert.NotNull(payload);
        Assert.Equal(0, payload!.Count);
    }

    [Fact]
    public async Task Counts_Only_Unread_Unresolved_Alerts_For_The_Route_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompany = Guid.NewGuid();

        await SeedAlertAsync(companyId);                       // counts
        await SeedAlertAsync(companyId);                       // counts
        await SeedAlertAsync(companyId, read: true);           // excluded - read
        await SeedAlertAsync(companyId, resolved: true);       // excluded - resolved
        await SeedAlertAsync(otherCompany);                    // excluded - other company

        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);
        var payload = await client.GetFromJsonAsync<CountPayload>(Url(companyId));

        Assert.Equal(2, payload!.Count);
    }

    [Fact]
    public async Task Marking_An_Alert_Read_Decrements_The_Count()
    {
        var companyId = Guid.NewGuid();
        var alertId = await SeedAlertAsync(companyId);
        await SeedAlertAsync(companyId);

        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);

        var before = await client.GetFromJsonAsync<CountPayload>(Url(companyId));
        Assert.Equal(2, before!.Count);

        var markResp = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/administrative-alerts/{alertId}/read", new { });
        markResp.EnsureSuccessStatusCode();

        var after = await client.GetFromJsonAsync<CountPayload>(Url(companyId));
        Assert.Equal(1, after!.Count);
    }

    private sealed record CountPayload(int Count);
}
