using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UpdateCompanySettingsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("eeeeeeee-0000-0000-0000-000000000007");

    public UpdateCompanySettingsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AuthenticatedClient(Guid tenantId, bool ensureActiveSubscription = true)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, UserId, SystemRoles.CompanyAdministrator, tenantId, ensureActiveSubscription);
        return client;
    }

    [Fact]
    public async Task Put_Company_Settings_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}/settings", new
        {
            timeZone = "UTC",
            locale = "en-GB",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Company_Settings_Updates_Settings_For_Authenticated_Request()
    {
        // The route companyId must match the caller's resolved tenant (UserProfile.CompanyId),
        // which TenantRouteAuthorizationMiddleware now enforces — seed the company under the
        // same fresh tenant id the caller is synced to, rather than an unrelated random id.
        var tenantId = Guid.NewGuid();
        using var client = await AuthenticatedClient(tenantId);

        // POST /api/companies (CreateCompany) was removed in 78a43344; seed the company directly
        // via CompaniesDbContext instead, mirroring TestRoleSeeder.EnsureActiveSubscriptionAsync.
        var createdCompanyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Settings Test {Guid.NewGuid():N}", companyId: tenantId);

        var response = await client.PutAsJsonAsync($"/api/companies/{createdCompanyId}/settings", new
        {
            timeZone = "Europe/London",
            locale = "en-GB",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rawJson = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("workingDays", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("employeeNumberMode", rawJson, StringComparison.OrdinalIgnoreCase);

        var payload = await response.Content.ReadFromJsonAsync<UpdateCompanySettingsPayload>();
        Assert.NotNull(payload);
        Assert.Equal(createdCompanyId, payload!.CompanyId);
        Assert.Equal("Europe/London", payload.TimeZone);
        Assert.Equal("en-GB", payload.Locale);
    }

    [Fact]
    public async Task Put_Company_Settings_Returns_UnprocessableEntity_When_TimeZone_Is_Blank()
    {
        // The route companyId must match the caller's resolved tenant (UserProfile.CompanyId),
        // which TenantRouteAuthorizationMiddleware now enforces — seed the company under the
        // same fresh tenant id the caller is synced to, rather than an unrelated random id.
        var tenantId = Guid.NewGuid();
        using var client = await AuthenticatedClient(tenantId);

        // POST /api/companies (CreateCompany) was removed in 78a43344; seed the company directly
        // via CompaniesDbContext instead, mirroring TestRoleSeeder.EnsureActiveSubscriptionAsync.
        var createdCompanyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Settings Test {Guid.NewGuid():N}", companyId: tenantId);

        var response = await client.PutAsJsonAsync($"/api/companies/{createdCompanyId}/settings", new
        {
            timeZone = string.Empty,
            locale = "en-GB",
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_Company_Settings_Returns_Forbidden_For_Unknown_Id()
    {
        // Under the SEC-001 tenant-isolation fix, a route companyId must match the caller's
        // resolved tenant, and CustomerSubscription has a hard FK to Company — so a subscription
        // can never exist without a real Company row for the same id. There is therefore no
        // reachable "own tenant, but company row is unexpectedly missing" 404 case for this
        // mutation endpoint any more: syncing the caller to a fresh tenant id with no seeded
        // Company/subscription now surfaces as ReadOnlyModeMiddleware's missing-subscription 403
        // before the handler's own lookup would ever run.
        var tenantId = Guid.NewGuid();
        using var client = await AuthenticatedClient(tenantId, ensureActiveSubscription: false);

        var response = await client.PutAsJsonAsync($"/api/companies/{tenantId}/settings", new
        {
            timeZone = "UTC",
            locale = "en-GB",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record UpdateCompanySettingsPayload(
        Guid CompanyId,
        string TimeZone,
        string Locale,
        DateTimeOffset UpdatedAt);
}
