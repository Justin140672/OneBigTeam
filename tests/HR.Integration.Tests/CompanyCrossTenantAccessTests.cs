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
/// Each test here proves the middleware guard specifically: a caller who holds a role that
/// would normally be *permitted* to call the endpoint (so a bare role/policy check alone would
/// return 200/OK) is still rejected with 403 Forbidden when the company GUID in the route
/// belongs to a different tenant than their own. The target company is always a real,
/// seeded company under a distinct tenant (never a random unused Guid), so a passing test
/// cannot be explained by the target simply not existing (which would 404, not 403).
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
    /// (<paramref name="ownTenantId"/>), which is deliberately a *different* company than the
    /// one the request will subsequently target.
    /// </summary>
    private async Task<HttpClient> ClientFor(Guid userId, Guid ownTenantId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, ownTenantId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, ownTenantId);
        return client;
    }

    // --- GetCompany ---

    [Fact]
    public async Task Get_Company_Returns_Forbidden_When_Company_Belongs_To_Another_Tenant()
    {
        var ownCompanyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Own {Guid.NewGuid():N}");
        var otherCompanyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Other {Guid.NewGuid():N}");
        using var client = await ClientFor(EmployeeUser, ownCompanyId);

        var response = await client.GetAsync($"/api/companies/{otherCompanyId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- UpdateCompany ---

    [Fact]
    public async Task Put_Company_Returns_Forbidden_When_Company_Belongs_To_Another_Tenant()
    {
        var ownCompanyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Own {Guid.NewGuid():N}");
        var otherCompanyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Other {Guid.NewGuid():N}");
        using var client = await ClientFor(CompanyAdminUser, ownCompanyId);

        var response = await client.PutAsJsonAsync($"/api/companies/{otherCompanyId}", new { name = "Hijacked" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- GetCompanySettings ---

    [Fact]
    public async Task Get_Company_Settings_Returns_Forbidden_When_Company_Belongs_To_Another_Tenant()
    {
        var ownCompanyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Own {Guid.NewGuid():N}");
        var otherCompanyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Other {Guid.NewGuid():N}");
        using var client = await ClientFor(EmployeeUser, ownCompanyId);

        var response = await client.GetAsync($"/api/companies/{otherCompanyId}/settings");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- UpdateCompanySettings ---

    [Fact]
    public async Task Put_Company_Settings_Returns_Forbidden_When_Company_Belongs_To_Another_Tenant()
    {
        var ownCompanyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Own {Guid.NewGuid():N}");
        var otherCompanyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Other {Guid.NewGuid():N}");
        using var client = await ClientFor(CompanyAdminUser, ownCompanyId);

        var response = await client.PutAsJsonAsync($"/api/companies/{otherCompanyId}/settings", new
        {
            timeZone = "UTC",
            locale = "en-GB",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- GetHrSettings ---

    [Fact]
    public async Task Get_Hr_Settings_Returns_Forbidden_When_Company_Belongs_To_Another_Tenant()
    {
        var ownCompanyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Own {Guid.NewGuid():N}");
        var otherCompanyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Other {Guid.NewGuid():N}");
        using var client = await ClientFor(EmployeeUser, ownCompanyId);

        var response = await client.GetAsync($"/api/companies/{otherCompanyId}/hr-settings");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- UpdateHrSettings ---

    [Fact]
    public async Task Put_Hr_Settings_Returns_Forbidden_When_Company_Belongs_To_Another_Tenant()
    {
        var ownCompanyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Own {Guid.NewGuid():N}");
        var otherCompanyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Other {Guid.NewGuid():N}");
        using var client = await ClientFor(HrAdminUser, ownCompanyId);

        var response = await client.PutAsJsonAsync($"/api/companies/{otherCompanyId}/hr-settings", new
        {
            workingDays = 31,
            hoursPerDay = 7.5,
            leaveYearStartMonth = 1,
            defaultHolidayAllowance = 25,
            probationMonths = 6
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- UploadCompanyLogo ---

    [Fact]
    public async Task Post_Company_Logo_Returns_Forbidden_When_Company_Belongs_To_Another_Tenant()
    {
        var ownCompanyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Own {Guid.NewGuid():N}");
        var otherCompanyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Other {Guid.NewGuid():N}");
        using var client = await ClientFor(CompanyAdminUser, ownCompanyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{otherCompanyId}/branding/logos/PrimaryLogo",
            new
            {
                fileName = "logo.png",
                contentType = "image/png",
                fileSizeBytes = 1024
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
