using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetCompanySettingsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("eeeeeeee-0000-0000-0000-000000000008");

    public GetCompanySettingsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AuthenticatedClient(Guid tenantId, bool ensureActiveSubscription = true)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, UserId, SystemRoles.Employee, tenantId, ensureActiveSubscription);
        return client;
    }

    [Fact]
    public async Task Get_Company_Settings_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Company_Settings_Returns_Slimmed_ProfileScoped_Fields_When_Never_Customised()
    {
        // The route companyId must match the caller's resolved tenant (UserProfile.CompanyId),
        // which TenantRouteAuthorizationMiddleware now enforces — seed the company under a
        // freshly generated tenant id and sync the caller to that same id, rather than an
        // unrelated one.
        var tenantId = Guid.NewGuid();
        using var client = await AuthenticatedClient(tenantId);

        // POST /api/companies (CreateCompany) was removed in 78a43344; seed the company directly
        // via CompaniesDbContext instead, mirroring TestRoleSeeder.EnsureActiveSubscriptionAsync.
        var createdCompanyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Settings Test {Guid.NewGuid():N}", companyId: tenantId);

        var response = await client.GetAsync($"/api/companies/{createdCompanyId}/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rawJson = await response.Content.ReadAsStringAsync();

        // Lock in that HR-policy fields no longer appear in the company-settings response.
        Assert.DoesNotContain("workingDays", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("employeeNumberMode", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("noticePeriodUnit", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("defaultAcknowledgementStatement", rawJson, StringComparison.OrdinalIgnoreCase);

        var payload = await response.Content.ReadFromJsonAsync<GetCompanySettingsPayload>();
        Assert.NotNull(payload);
        Assert.Equal(createdCompanyId, payload!.CompanyId);
        Assert.Equal("UTC", payload.TimeZone);
        Assert.Equal("en-GB", payload.Locale);
        Assert.False(string.IsNullOrEmpty(payload.PostcodeRegex));
        Assert.False(string.IsNullOrEmpty(payload.TelephoneRegex));
        Assert.False(string.IsNullOrEmpty(payload.MobileRegex));
    }

    [Fact]
    public async Task Get_Company_Settings_Returns_NotFound_For_Unknown_Id()
    {
        // Route companyId must match the caller's resolved tenant to pass tenant-route
        // authorization; sync the caller to a fresh tenant id for which no Company row was ever
        // seeded, so the request is authorized but the endpoint's own lookup 404s.
        var tenantId = Guid.NewGuid();
        using var client = await AuthenticatedClient(tenantId, ensureActiveSubscription: false);

        var response = await client.GetAsync($"/api/companies/{tenantId}/settings");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record GetCompanySettingsPayload(
        Guid CompanyId,
        string TimeZone,
        string Locale,
        string PostcodeRegex,
        string TelephoneRegex,
        string MobileRegex,
        DateTimeOffset UpdatedAt);
}
