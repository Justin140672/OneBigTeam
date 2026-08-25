using HR.Modules.Employees.Contracts;
using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetHrSettingsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("eeeeeeee-1111-0000-0000-000000000001");

    public GetHrSettingsEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Get_Hr_Settings_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/hr-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Hr_Settings_Returns_OK_For_Employee_Role_Reading_Own_Company()
    {
        // The route companyId must match the caller's resolved tenant (UserProfile.CompanyId),
        // which TenantRouteAuthorizationMiddleware now enforces — seed the company under the
        // same fresh tenant id the caller is synced to, rather than an unrelated random id.
        var tenantId = Guid.NewGuid();
        using var client = await AuthenticatedClient(tenantId);

        // POST /api/companies (CreateCompany) was removed in 78a43344; seed the company directly
        // via CompaniesDbContext instead, mirroring TestRoleSeeder.EnsureActiveSubscriptionAsync.
        var createdCompanyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Hr Settings Test {Guid.NewGuid():N}", companyId: tenantId);

        var response = await client.GetAsync($"/api/companies/{createdCompanyId}/hr-settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<GetHrSettingsPayload>();
        Assert.NotNull(payload);
        Assert.Equal(createdCompanyId, payload!.CompanyId);
        Assert.Equal("Automatic", payload.EmployeeNumberMode);
        Assert.Equal(1, payload.NextEmployeeNumber);

        // SET-04: newly-provisioned companies retain the CompanySettings.CreateDefault checkpoint
        // and threshold defaults until explicitly changed.
        Assert.Equal(30, payload.ProbationCheckpointDay1);
        Assert.Equal(60, payload.ProbationCheckpointDay2);
        Assert.Equal(90, payload.ProbationCheckpointDay3);
        Assert.Equal(4, payload.FrequentAbsenceCountThreshold);
        Assert.Equal(365, payload.FrequentAbsenceWindowDays);
        Assert.Equal(28, payload.LongAbsenceDayThreshold);
        Assert.Equal(3, payload.WeekdayPatternOccurrenceThreshold);
        Assert.Equal(365, payload.WeekdayPatternWindowDays);
    }

    [Fact]
    public async Task Get_Hr_Settings_Returns_NotFound_For_Unknown_Id()
    {
        // Route companyId must match the caller's resolved tenant to pass tenant-route
        // authorization; sync the caller to a fresh tenant id for which no Company row was ever
        // seeded, so the request is authorized but the endpoint's own lookup 404s.
        var tenantId = Guid.NewGuid();
        using var client = await AuthenticatedClient(tenantId, ensureActiveSubscription: false);

        var response = await client.GetAsync($"/api/companies/{tenantId}/hr-settings");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record GetHrSettingsPayload(
        Guid CompanyId,
        int WorkingDays,
        decimal HoursPerDay,
        int LeaveYearStartMonth,
        decimal DefaultHolidayAllowance,
        int ProbationMonths,
        bool ExcludePublicHolidaysFromLeave,
        bool ExcludePublicHolidaysFromSickness,
        bool DisplaySalaryOnEmployeeProfile,
        int FitNoteRequiredAfterDays,
        int ReturnToWorkRequiredAfterDays,
        string DefaultAcknowledgementStatement,
        int AcknowledgementReminderIntervalDays,
        string NoticePeriodUnit,
        int NoticePeriodLength,
        bool AutoDisableAccessOnLeavingDate,
        string EmployeeNumberMode,
        string? EmployeeNumberPrefix,
        int NextEmployeeNumber,
        int EmployeeNumberMinimumLength,
        DateTimeOffset UpdatedAt,
        int? ProbationCheckpointDay1,
        int? ProbationCheckpointDay2,
        int? ProbationCheckpointDay3,
        int FrequentAbsenceCountThreshold,
        int FrequentAbsenceWindowDays,
        int LongAbsenceDayThreshold,
        int WeekdayPatternOccurrenceThreshold,
        int WeekdayPatternWindowDays);
}
