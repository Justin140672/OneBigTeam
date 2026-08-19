using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// Proves the company:manage FastEndpoints policy actually enforces access end-to-end
/// over real HTTP for UpdateCompany, UpdateCompanySettings, and UploadCompanyLogo.
/// Company Administrator is the only role permitted to change company-level
/// configuration. HR Administrator is a distinct role — broadly privileged over
/// employee/leave/sickness data elsewhere — and must be denied here; that's the
/// specific regression this file guards against.
/// </summary>
[Collection("Integration")]
public class CompanyAuthorizationTests
{
    private readonly ApiWebApplicationFactory _factory;

    // Guid.NewGuid() rather than a hardcoded literal — under the shared-database test
    // collection, a hardcoded id here previously collided with the same literal used (and
    // assigned a role) in another test file, silently granting this "no role" user a role.
    private static readonly Guid NoRoleUser = Guid.NewGuid();
    private static readonly Guid EmployeeUser = new("cc000001-0000-0000-0000-000000000002");
    private static readonly Guid ManagerUser = new("cc000001-0000-0000-0000-000000000003");
    private static readonly Guid RecruiterUser = new("cc000001-0000-0000-0000-000000000004");
    private static readonly Guid HrAdminUser = new("cc000001-0000-0000-0000-000000000006");
    private static readonly Guid CompanyAdminUser = new("cc000001-0000-0000-0000-000000000007");

    public CompanyAuthorizationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, EmployeeUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, ManagerUser, SystemRoles.Manager);
            await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminUser, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientFor(Guid tenantId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, tenantId);
        return client;
    }

    // POST /api/companies (CreateCompany) was removed in 78a43344; this now provisions the
    // company directly via CompaniesDbContext, mirroring TestRoleSeeder.EnsureActiveSubscriptionAsync.
    private async Task<Guid> CreateCompanyAsync(Guid tenantId)
    {
        return await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Auth Test {Guid.NewGuid():N}", companyId: tenantId);
    }

    // --- UpdateCompany ---

    [Fact]
    public async Task Put_Company_Returns_Forbidden_For_User_With_No_Roles()
    {
        using var client = await ClientFor(Guid.NewGuid(), NoRoleUser);

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}", new { name = "X" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Company_Returns_Forbidden_For_Employee_Role()
    {
        using var client = await ClientFor(Guid.NewGuid(), EmployeeUser);

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}", new { name = "X" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Company_Returns_Forbidden_For_Manager_Role()
    {
        using var client = await ClientFor(Guid.NewGuid(), ManagerUser);

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}", new { name = "X" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Company_Returns_Forbidden_For_Recruiter_Role()
    {
        using var client = await ClientFor(Guid.NewGuid(), RecruiterUser);

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}", new { name = "X" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Company_Returns_Forbidden_For_HrAdministrator_Role()
    {
        using var client = await ClientFor(Guid.NewGuid(), HrAdminUser);

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}", new { name = "X" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Company_Succeeds_For_CompanyAdministrator_Role()
    {
        var tenantId = Guid.NewGuid();
        var companyId = await CreateCompanyAsync(tenantId);
        using var client = await ClientFor(tenantId, CompanyAdminUser);

        var response = await client.PutAsJsonAsync($"/api/companies/{companyId}", new
        {
            name = "Updated By Company Admin",
            addresses = new[]
            {
                new { type = "RegisteredOffice", line1 = "10 High Street", city = "London", countryCode = "GB" }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --- UpdateCompanySettings ---

    [Fact]
    public async Task Put_Company_Settings_Returns_Forbidden_For_User_With_No_Roles()
    {
        using var client = await ClientFor(Guid.NewGuid(), NoRoleUser);

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}/settings", SettingsBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Company_Settings_Returns_Forbidden_For_Employee_Role()
    {
        using var client = await ClientFor(Guid.NewGuid(), EmployeeUser);

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}/settings", SettingsBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Company_Settings_Returns_Forbidden_For_Manager_Role()
    {
        using var client = await ClientFor(Guid.NewGuid(), ManagerUser);

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}/settings", SettingsBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Company_Settings_Returns_Forbidden_For_HrAdministrator_Role()
    {
        using var client = await ClientFor(Guid.NewGuid(), HrAdminUser);

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}/settings", SettingsBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Company_Settings_Succeeds_For_CompanyAdministrator_Role()
    {
        var tenantId = Guid.NewGuid();
        var companyId = await CreateCompanyAsync(tenantId);
        using var client = await ClientFor(tenantId, CompanyAdminUser);

        var response = await client.PutAsJsonAsync($"/api/companies/{companyId}/settings", SettingsBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static object SettingsBody() => new
    {
        timeZone = "UTC",
        locale = "en-GB",
    };

    // --- UpdateHrSettings ---

    [Fact]
    public async Task Put_Hr_Settings_Returns_Forbidden_For_User_With_No_Roles()
    {
        using var client = await ClientFor(Guid.NewGuid(), NoRoleUser);

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}/hr-settings", HrSettingsBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Hr_Settings_Returns_Forbidden_For_Employee_Role()
    {
        using var client = await ClientFor(Guid.NewGuid(), EmployeeUser);

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}/hr-settings", HrSettingsBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Hr_Settings_Returns_Forbidden_For_Manager_Role()
    {
        using var client = await ClientFor(Guid.NewGuid(), ManagerUser);

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}/hr-settings", HrSettingsBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Hr_Settings_Returns_Forbidden_For_CompanyAdministrator_Role()
    {
        // Mirrors the company:manage vs employee:manage separation above, but inverted:
        // HR Administrator is the only role permitted to change HR-policy settings, and
        // Company Administrator (without HR Administrator) must now be denied here — this
        // is the specific authorization-gap fix this file guards against.
        var tenantId = Guid.NewGuid();
        var companyId = await CreateCompanyAsync(tenantId);
        using var client = await ClientFor(tenantId, CompanyAdminUser);

        var response = await client.PutAsJsonAsync($"/api/companies/{companyId}/hr-settings", HrSettingsBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Hr_Settings_Succeeds_For_HrAdministrator_Role()
    {
        var tenantId = Guid.NewGuid();
        var companyId = await CreateCompanyAsync(tenantId);
        using var client = await ClientFor(tenantId, HrAdminUser);

        var response = await client.PutAsJsonAsync($"/api/companies/{companyId}/hr-settings", HrSettingsBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static object HrSettingsBody() => new
    {
        workingDays = 31,
        hoursPerDay = 7.5,
        leaveYearStartMonth = 1,
        defaultHolidayAllowance = 25,
        probationMonths = 6
    };

    // --- UploadCompanyLogo ---

    [Fact]
    public async Task Post_Company_Logo_Returns_Forbidden_For_User_With_No_Roles()
    {
        using var client = await ClientFor(Guid.NewGuid(), NoRoleUser);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/branding/logos/PrimaryLogo", LogoBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Company_Logo_Returns_Forbidden_For_Employee_Role()
    {
        using var client = await ClientFor(Guid.NewGuid(), EmployeeUser);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/branding/logos/PrimaryLogo", LogoBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Company_Logo_Returns_Forbidden_For_Manager_Role()
    {
        using var client = await ClientFor(Guid.NewGuid(), ManagerUser);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/branding/logos/PrimaryLogo", LogoBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Company_Logo_Returns_Forbidden_For_HrAdministrator_Role()
    {
        using var client = await ClientFor(Guid.NewGuid(), HrAdminUser);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/branding/logos/PrimaryLogo", LogoBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Company_Logo_Succeeds_For_CompanyAdministrator_Role()
    {
        var tenantId = Guid.NewGuid();
        var companyId = await CreateCompanyAsync(tenantId);
        using var client = await ClientFor(tenantId, CompanyAdminUser);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/branding/logos/PrimaryLogo", LogoBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static object LogoBody() => new
    {
        fileName = "logo.png",
        contentType = "image/png",
        fileSizeBytes = 1024
    };

    private sealed record CreatedCompanyPayload(Guid Id);
}
