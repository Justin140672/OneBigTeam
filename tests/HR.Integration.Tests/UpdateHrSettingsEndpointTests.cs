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

    private async Task<HttpClient> ClientFor(Guid userId, Guid tenantId, bool ensureActiveSubscription = true)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, tenantId, ensureActiveSubscription);
        return client;
    }

    // POST /api/companies (CreateCompany) was removed in 78a43344; this now provisions the
    // company directly via CompaniesDbContext, mirroring TestRoleSeeder.EnsureActiveSubscriptionAsync.
    // The route companyId must match the caller's resolved tenant (UserProfile.CompanyId), which
    // TenantRouteAuthorizationMiddleware now enforces — so the company is seeded under the same
    // tenantId the caller is synced to via ClientFor, not an unrelated random id.
    private async Task<Guid> CreateCompanyAsync(Guid tenantId)
    {
        return await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Hr Settings Test {Guid.NewGuid():N}", companyId: tenantId);
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
    public async Task Put_Hr_Settings_Returns_UnprocessableEntity_When_ProbationMonths_Is_Zero()
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
            probationMonths = 0,
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_Hr_Settings_Returns_Ok_When_DefaultHolidayAllowance_Is_Zero()
    {
        var tenantId = Guid.NewGuid();
        var companyId = await CreateCompanyAsync(tenantId);
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var response = await client.PutAsJsonAsync($"/api/companies/{companyId}/hr-settings", new
        {
            workingDays = 31,
            hoursPerDay = 7.5,
            leaveYearStartMonth = 1,
            defaultHolidayAllowance = 0,
            probationMonths = 6,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<UpdateHrSettingsPayload>();
        Assert.NotNull(payload);
        Assert.Equal(0, payload!.DefaultHolidayAllowance);
    }

    [Fact]
    public async Task Put_Hr_Settings_Returns_Conflict_When_Version_Is_Stale()
    {
        var tenantId = Guid.NewGuid();
        var companyId = await CreateCompanyAsync(tenantId);
        using var client = await ClientFor(HrAdminUserId, tenantId);

        var firstResponse = await client.PutAsJsonAsync($"/api/companies/{companyId}/hr-settings", new
        {
            workingDays = 31,
            hoursPerDay = 7.5,
            leaveYearStartMonth = 1,
            defaultHolidayAllowance = 25,
            probationMonths = 6,
            version = 1,
        });
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var firstPayload = await firstResponse.Content.ReadFromJsonAsync<UpdateHrSettingsPayload>();
        // UpdateHrSettingsHandler calls both UpdateHrPolicy and UpdateAssetNumberSettings, each of
        // which increments Version once — a single successful HR-settings update therefore bumps
        // Version by 2 (1 -> 3), not 1.
        Assert.Equal(3, firstPayload!.Version);

        var secondResponse = await client.PutAsJsonAsync($"/api/companies/{companyId}/hr-settings", new
        {
            workingDays = 31,
            hoursPerDay = 7.5,
            leaveYearStartMonth = 4,
            defaultHolidayAllowance = 28,
            probationMonths = 3,
            version = 1,
        });

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Put_Hr_Settings_Returns_Version_Incremented_On_Success()
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
        Assert.Equal(3, payload!.Version);
    }

    [Fact]
    public async Task Put_Hr_Settings_Returns_Forbidden_For_Unknown_Id()
    {
        // Under the SEC-001 tenant-isolation fix, a route companyId must match the caller's
        // resolved tenant, and CustomerSubscription has a hard FK to Company — so a subscription
        // can never exist without a real Company row for the same id. There is therefore no
        // reachable "own tenant, but company row is unexpectedly missing" 404 case for this
        // mutation endpoint any more: syncing the caller to a fresh tenant id with no seeded
        // Company/subscription now surfaces as ReadOnlyModeMiddleware's missing-subscription 403
        // before the handler's own lookup would ever run.
        var tenantId = Guid.NewGuid();
        using var client = await ClientFor(HrAdminUserId, tenantId, ensureActiveSubscription: false);

        var response = await client.PutAsJsonAsync($"/api/companies/{tenantId}/hr-settings", HrSettingsBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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

        // Update HR settings — hr-settings:manage is HrAdministrator-only. The prior company-profile
        // update above created the settings row (Version 1) and incremented it to 2, so this
        // request must supply Version 2 to avoid a SET-03 concurrency conflict.
        var hrResponse = await client.PutAsJsonAsync($"/api/companies/{companyId}/hr-settings", new
        {
            workingDays = 31,
            hoursPerDay = 7.5,
            leaveYearStartMonth = 4,
            defaultHolidayAllowance = 28,
            probationMonths = 3,
            version = 2,
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
        decimal DefaultHolidayAllowance,
        int Version);

    private sealed record GetCompanySettingsPayload(
        Guid CompanyId,
        string TimeZone,
        string Locale);

    private sealed record GetHrSettingsPayload(
        Guid CompanyId,
        int LeaveYearStartMonth,
        decimal DefaultHolidayAllowance);
}
