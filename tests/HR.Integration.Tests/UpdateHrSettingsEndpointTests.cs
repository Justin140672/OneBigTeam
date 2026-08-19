using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class UpdateHrSettingsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid HrAdminUserId = new("eeeeeeee-1111-0000-0000-000000000002");
    private static readonly Guid CompanyAdminOnlyUserId = new("eeeeeeee-1111-0000-0000-000000000003");

    public UpdateHrSettingsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUserId, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminOnlyUserId, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminOnlyUserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientFor(Guid userId, Guid tenantId)
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
        _ = tenantId;
        return await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Hr Settings Test {Guid.NewGuid():N}");
    }

    private static object HrSettingsBody() => new
    {
        workingDays = 31,
        hoursPerDay = 7.5,
        leaveYearStartMonth = 1,
        defaultHolidayAllowance = 25,
        probationMonths = 6
    };

    [Fact]
    public async Task Put_Hr_Settings_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}/hr-settings", HrSettingsBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Hr_Settings_Succeeds_For_HrAdministrator_Role()
    {
        var tenantId = Guid.NewGuid();
        var companyId = await CreateCompanyAsync(tenantId);
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var response = await client.PutAsJsonAsync($"/api/companies/{companyId}/hr-settings", new
        {
            workingDays = 31,
            hoursPerDay = 7.5,
            leaveYearStartMonth = 4,
            defaultHolidayAllowance = 28,
            probationMonths = 3,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<UpdateHrSettingsPayload>();
        Assert.NotNull(payload);
        Assert.Equal(companyId, payload!.CompanyId);
        Assert.Equal(4, payload.LeaveYearStartMonth);
        Assert.Equal(28, payload.DefaultHolidayAllowance);
    }

    [Fact]
    public async Task Put_Hr_Settings_Returns_Forbidden_For_CompanyAdministrator_Only_Role()
    {
        // Key authorization-gap fix: Company Administrator alone (without HrAdministrator)
        // must no longer be able to change HR-policy settings.
        var tenantId = Guid.NewGuid();
        var companyId = await CreateCompanyAsync(tenantId);
        using var client = await ClientFor(CompanyAdminOnlyUserId, tenantId);

        var response = await client.PutAsJsonAsync($"/api/companies/{companyId}/hr-settings", HrSettingsBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Hr_Settings_Returns_UnprocessableEntity_When_NoticePeriodLength_Is_Not_Positive()
    {
        var tenantId = Guid.NewGuid();
        var companyId = await CreateCompanyAsync(tenantId);
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var response = await client.PutAsJsonAsync($"/api/companies/{companyId}/hr-settings", new
        {
            workingDays = 31,
            hoursPerDay = 7.5,
            leaveYearStartMonth = 1,
            defaultHolidayAllowance = 25,
            probationMonths = 6,
            noticePeriodUnit = "Months",
            noticePeriodLength = 0
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_Hr_Settings_Returns_UnprocessableEntity_When_WorkingDays_Is_None()
    {
        var tenantId = Guid.NewGuid();
        var companyId = await CreateCompanyAsync(tenantId);
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var response = await client.PutAsJsonAsync($"/api/companies/{companyId}/hr-settings", new
        {
            workingDays = 0,
            hoursPerDay = 7.5,
            leaveYearStartMonth = 1,
            defaultHolidayAllowance = 25,
            probationMonths = 6,
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_Hr_Settings_Returns_NotFound_For_Unknown_Id()
    {
        using var client = await ClientFor(HrAdminUserId, Guid.NewGuid());

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}/hr-settings", HrSettingsBody());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Updating_CompanySettings_And_HrSettings_Independently_Preserves_Each_Sections_Fields()
    {
        var tenantId = Guid.NewGuid();
        var companyId = await CreateCompanyAsync(tenantId);
        using var client = await ClientFor(HrAdminUserId, tenantId);
        using var companyAdminClient = await ClientFor(CompanyAdminOnlyUserId, tenantId);

        // Update company (profile) settings — company:manage is CompanyAdministrator-only.
        var settingsResponse = await companyAdminClient.PutAsJsonAsync($"/api/companies/{companyId}/settings", new
        {
            timeZone = "Europe/London",
            locale = "en-GB",
        });
        Assert.Equal(HttpStatusCode.OK, settingsResponse.StatusCode);

        // Update HR settings — hr-settings:manage is HrAdministrator-only.
        var hrResponse = await client.PutAsJsonAsync($"/api/companies/{companyId}/hr-settings", new
        {
            workingDays = 31,
            hoursPerDay = 7.5,
            leaveYearStartMonth = 4,
            defaultHolidayAllowance = 28,
            probationMonths = 3,
        });
        Assert.Equal(HttpStatusCode.OK, hrResponse.StatusCode);

        var getSettingsResponse = await client.GetAsync($"/api/companies/{companyId}/settings");
        Assert.Equal(HttpStatusCode.OK, getSettingsResponse.StatusCode);
        var settingsPayload = await getSettingsResponse.Content.ReadFromJsonAsync<GetCompanySettingsPayload>();
        Assert.NotNull(settingsPayload);
        Assert.Equal("Europe/London", settingsPayload!.TimeZone);

        var getHrResponse = await client.GetAsync($"/api/companies/{companyId}/hr-settings");
        Assert.Equal(HttpStatusCode.OK, getHrResponse.StatusCode);
        var hrPayload = await getHrResponse.Content.ReadFromJsonAsync<GetHrSettingsPayload>();
        Assert.NotNull(hrPayload);
        Assert.Equal(4, hrPayload!.LeaveYearStartMonth);
        Assert.Equal(28, hrPayload.DefaultHolidayAllowance);

        // Company (profile) settings must remain untouched by the HR settings update.
        var getSettingsAgain = await client.GetAsync($"/api/companies/{companyId}/settings");
        var settingsPayloadAgain = await getSettingsAgain.Content.ReadFromJsonAsync<GetCompanySettingsPayload>();
        Assert.Equal("Europe/London", settingsPayloadAgain!.TimeZone);
    }

    private sealed record CreatedCompanyPayload(Guid Id);

    private sealed record UpdateHrSettingsPayload(
        Guid CompanyId,
        int LeaveYearStartMonth,
        decimal DefaultHolidayAllowance);

    private sealed record GetCompanySettingsPayload(
        Guid CompanyId,
        string TimeZone,
        string Locale);

    private sealed record GetHrSettingsPayload(
        Guid CompanyId,
        int LeaveYearStartMonth,
        decimal DefaultHolidayAllowance);
}
