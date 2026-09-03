using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// SEC-001 regression coverage: seven Companies-module endpoints previously routed the
/// company's own id through a segment named <c>{id:guid}</c> instead of
/// <c>{companyId:guid}</c>, so <see cref="HR.Modules.Identity.TenantRouteAuthorizationMiddleware"/>
/// (which only enforces tenant isolation when a route contains a value literally named
/// "companyId") silently ignored them — any authenticated user could read/mutate another
/// tenant's company by supplying its GUID, regardless of role/policy checks.
///
/// Each test here proves the middleware guard specifically for one endpoint, asserting BOTH:
///   1. a caller holding a role that would normally be *permitted* to call the endpoint is still
///      rejected with 403 Forbidden when the company GUID in the route belongs to a *different*
///      tenant than their own (the SEC-001 hole); and
///   2. the exact same caller/role succeeds (2xx) when the route targets *their own* tenant — so
///      the 403 above is attributable to the tenant guard, not to a blanket authorization failure
///      or a misconfigured request.
///
/// The cross-tenant target company is always a real, seeded company under a distinct tenant
/// (never a random unused Guid), so a passing test cannot be explained by the target simply not
/// existing (which would 404, not 403).
///
/// <para>
/// REQUIRES DOCKER: this class boots the full API via <see cref="ApiWebApplicationFactory"/>,
/// which starts a real PostgreSQL container. It is serialized through the "Integration"
/// collection and is skipped implicitly on hosts without a working Docker daemon.
/// </para>
/// </summary>
[Collection("Integration")]
public class CompanyCrossTenantAccessTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid CompanyAdminUser = new("dd000001-0000-0000-0000-000000000001");
    private static readonly Guid HrAdminUser = new("dd000001-0000-0000-0000-000000000002");
    private static readonly Guid EmployeeUser = new("dd000001-0000-0000-0000-000000000003");

    public CompanyCrossTenantAccessTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminUser, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, EmployeeUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Builds a client for <paramref name="userId"/> resolved against their own tenant
    /// (<paramref name="ownTenantId"/>).
    /// </summary>
    private async Task<HttpClient> ClientFor(Guid userId, Guid ownTenantId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, ownTenantId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, ownTenantId);
        return client;
    }

    private async Task<(Guid ownTenantId, Guid otherCompanyId)> SeedOwnAndOtherAsync()
    {
        var ownTenantId = Guid.NewGuid();
        // Seed the caller's own company under the same id their tenant resolves to, so
        // TenantRouteAuthorizationMiddleware permits the own-tenant leg of each test.
        await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Own {Guid.NewGuid():N}", companyId: ownTenantId);
        var otherCompanyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Other {Guid.NewGuid():N}");
        return (ownTenantId, otherCompanyId);
    }

    private static readonly object HrSettingsBody = new
    {
        workingDays = 31,
        hoursPerDay = 7.5,
        leaveYearStartMonth = 1,
        defaultHolidayAllowance = 25,
        probationMonths = 6,
    };

    private static readonly object CompanySettingsBody = new
    {
        timeZone = "Europe/London",
        locale = "en-GB",
    };

    private static object UpdateCompanyBody => new
    {
        name = "Updated Company",
        addresses = new[]
        {
            new { type = "RegisteredOffice", line1 = "10 High Street", city = "London", postalCode = (string?)"SW1A 1AA", countryCode = "GB" },
            new { type = "TradingAddress", line1 = "11 Billing Street", city = "Manchester", postalCode = (string?)null, countryCode = "GB" },
        },
    };

    private static readonly object LogoBody = new
    {
        fileName = "logo.png",
        contentType = "image/png",
        fileSizeBytes = 1024,
    };

    // --- GetCompany ---

    [Fact]
    public async Task Get_Company_Enforces_Tenant_Isolation()
    {
        var (ownTenantId, otherCompanyId) = await SeedOwnAndOtherAsync();
        using var client = await ClientFor(EmployeeUser, ownTenantId);

        var crossTenant = await client.GetAsync($"/api/companies/{otherCompanyId}");
        Assert.Equal(HttpStatusCode.Forbidden, crossTenant.StatusCode);

        var ownTenant = await client.GetAsync($"/api/companies/{ownTenantId}");
        Assert.True(ownTenant.IsSuccessStatusCode, $"Expected 2xx for own tenant, got {(int)ownTenant.StatusCode}.");
    }

    // --- UpdateCompany ---

    [Fact]
    public async Task Put_Company_Enforces_Tenant_Isolation()
    {
        var (ownTenantId, otherCompanyId) = await SeedOwnAndOtherAsync();
        using var client = await ClientFor(CompanyAdminUser, ownTenantId);

        var crossTenant = await client.PutAsJsonAsync($"/api/companies/{otherCompanyId}", UpdateCompanyBody);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenant.StatusCode);

        var ownTenant = await client.PutAsJsonAsync($"/api/companies/{ownTenantId}", UpdateCompanyBody);
        Assert.True(ownTenant.IsSuccessStatusCode, $"Expected 2xx for own tenant, got {(int)ownTenant.StatusCode}.");
    }

    // --- GetCompanySettings ---

    [Fact]
    public async Task Get_Company_Settings_Enforces_Tenant_Isolation()
    {
        var (ownTenantId, otherCompanyId) = await SeedOwnAndOtherAsync();
        using var client = await ClientFor(EmployeeUser, ownTenantId);

        var crossTenant = await client.GetAsync($"/api/companies/{otherCompanyId}/settings");
        Assert.Equal(HttpStatusCode.Forbidden, crossTenant.StatusCode);

        var ownTenant = await client.GetAsync($"/api/companies/{ownTenantId}/settings");
        Assert.True(ownTenant.IsSuccessStatusCode, $"Expected 2xx for own tenant, got {(int)ownTenant.StatusCode}.");
    }

    // --- UpdateCompanySettings ---

    [Fact]
    public async Task Put_Company_Settings_Enforces_Tenant_Isolation()
    {
        var (ownTenantId, otherCompanyId) = await SeedOwnAndOtherAsync();
        using var client = await ClientFor(CompanyAdminUser, ownTenantId);

        var crossTenant = await client.PutAsJsonAsync($"/api/companies/{otherCompanyId}/settings", CompanySettingsBody);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenant.StatusCode);

        var ownTenant = await client.PutAsJsonAsync($"/api/companies/{ownTenantId}/settings", CompanySettingsBody);
        Assert.True(ownTenant.IsSuccessStatusCode, $"Expected 2xx for own tenant, got {(int)ownTenant.StatusCode}.");
    }

    // --- GetHrSettings ---

    [Fact]
    public async Task Get_Hr_Settings_Enforces_Tenant_Isolation()
    {
        var (ownTenantId, otherCompanyId) = await SeedOwnAndOtherAsync();
        using var client = await ClientFor(EmployeeUser, ownTenantId);

        var crossTenant = await client.GetAsync($"/api/companies/{otherCompanyId}/hr-settings");
        Assert.Equal(HttpStatusCode.Forbidden, crossTenant.StatusCode);

        var ownTenant = await client.GetAsync($"/api/companies/{ownTenantId}/hr-settings");
        Assert.True(ownTenant.IsSuccessStatusCode, $"Expected 2xx for own tenant, got {(int)ownTenant.StatusCode}.");
    }

    // --- UpdateHrSettings ---

    [Fact]
    public async Task Put_Hr_Settings_Enforces_Tenant_Isolation()
    {
        var (ownTenantId, otherCompanyId) = await SeedOwnAndOtherAsync();
        using var client = await ClientFor(HrAdminUser, ownTenantId);

        var crossTenant = await client.PutAsJsonAsync($"/api/companies/{otherCompanyId}/hr-settings", HrSettingsBody);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenant.StatusCode);

        var ownTenant = await client.PutAsJsonAsync($"/api/companies/{ownTenantId}/hr-settings", HrSettingsBody);
        Assert.True(ownTenant.IsSuccessStatusCode, $"Expected 2xx for own tenant, got {(int)ownTenant.StatusCode}.");
    }

    // --- UploadCompanyLogo ---

    [Fact]
    public async Task Post_Company_Logo_Enforces_Tenant_Isolation()
    {
        var (ownTenantId, otherCompanyId) = await SeedOwnAndOtherAsync();
        using var client = await ClientFor(CompanyAdminUser, ownTenantId);

        var crossTenant = await client.PostAsJsonAsync(
            $"/api/companies/{otherCompanyId}/branding/logos/PrimaryLogo", LogoBody);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenant.StatusCode);

        var ownTenant = await client.PostAsJsonAsync(
            $"/api/companies/{ownTenantId}/branding/logos/PrimaryLogo", LogoBody);
        Assert.True(ownTenant.IsSuccessStatusCode, $"Expected 2xx for own tenant, got {(int)ownTenant.StatusCode}.");
    }
}
