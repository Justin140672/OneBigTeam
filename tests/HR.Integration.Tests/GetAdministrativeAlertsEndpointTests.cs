using System.Net;
using System.Net.Http.Json;
using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// ADM-03: the shared administrative alerts inbox list endpoint. Gated by <c>admin-alerts:view</c>
/// (HR Administrator only) and company scoped by the route company id.
/// </summary>
[Collection("Integration")]
public class GetAdministrativeAlertsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public GetAdministrativeAlertsEndpointTests(ApiWebApplicationFactory factory) => _factory = factory;

    private static string Url(Guid companyId) => $"/api/companies/{companyId}/administrative-alerts";

    private async Task<HttpClient> ClientFor(Guid companyId, Guid userId, Guid roleId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, roleId, companyId);
        return client;
    }

    private async Task SeedAlertAsync(Guid companyId, string dedupKey, AdministrativeAlertSeverity severity = AdministrativeAlertSeverity.Warning)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        db.AdministrativeAlerts.Add(AdministrativeAlert.Raise(Guid.NewGuid(), new RaiseAdministrativeAlertCommand(
            companyId, severity, AdministrativeAlertCategory.IntegrationDelivery,
            "Delivery failing", "detail", Now, dedupKey, null, null, null, null), Now));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Returns_Unauthorized_For_Anonymous()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(Url(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public static IEnumerable<object[]> ForbiddenRoles() => new[]
    {
        new object[] { SystemRoles.Employee },
        new object[] { SystemRoles.Manager },
        new object[] { SystemRoles.Recruiter },
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
    public async Task Returns_Ok_And_Empty_State_For_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);

        var payload = await client.GetFromJsonAsync<AlertsPayload>(Url(companyId));

        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
        Assert.Equal(0, payload.TotalCount);
        Assert.Equal(0, payload.UnreadCount);
    }

    [Fact]
    public async Task Company_Isolation_HrAdmin_Sees_Only_Own_Company_Alerts()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        await SeedAlertAsync(companyA, "a:1");
        await SeedAlertAsync(companyA, "a:2");
        await SeedAlertAsync(companyB, "b:1");

        using var client = await ClientFor(companyA, Guid.NewGuid(), SystemRoles.HrAdministrator);
        var payload = await client.GetFromJsonAsync<AlertsPayload>(Url(companyA));

        Assert.NotNull(payload);
        Assert.Equal(2, payload!.TotalCount);
        Assert.Equal(2, payload.Items.Count);
    }

    [Fact]
    public async Task Returns_UnprocessableEntity_For_Invalid_Severity()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);

        var response = await client.GetAsync(Url(companyId) + "?severity=bogus");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Returns_UnprocessableEntity_For_Out_Of_Range_PageSize()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);

        var response = await client.GetAsync(Url(companyId) + "?pageSize=999");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record AlertsPayload(
        int UnreadCount, List<AlertItemPayload> Items, int TotalCount, int PageNumber, int PageSize, int TotalPages);

    private sealed record AlertItemPayload(Guid Id, string Severity, string Category, string Summary, string Status, bool IsRead);
}
