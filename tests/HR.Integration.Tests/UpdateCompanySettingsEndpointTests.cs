using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class UpdateCompanySettingsEndpointTests : IClassFixture<ApiWebApplicationFactory>
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

    private HttpClient AuthenticatedClient(Guid tenantId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
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
            workingDays = 31,
            hoursPerDay = 7.5,
            leaveYearStartMonth = 1,
            defaultHolidayAllowance = 25,
            probationMonths = 6
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Company_Settings_Updates_Settings_For_Authenticated_Request()
    {
        using var client = AuthenticatedClient(UserId);

        var createResponse = await client.PostAsJsonAsync("/api/companies", new
        {
            name = $"Settings Test {Guid.NewGuid():N}",
            addresses = new[]
            {
                new { type = "RegisteredOffice", line1 = "10 High Street", city = "London", countryCode = "GB" }
            }
        });
        createResponse.EnsureSuccessStatusCode();

        var createdCompany = await createResponse.Content.ReadFromJsonAsync<CreateCompanyPayload>();
        Assert.NotNull(createdCompany);

        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, createdCompany!.Id.ToString());

        var response = await client.PutAsJsonAsync($"/api/companies/{createdCompany.Id}/settings", new
        {
            timeZone = "Europe/London",
            locale = "en-GB",
            workingDays = 31,
            hoursPerDay = 7.5,
            leaveYearStartMonth = 4,
            defaultHolidayAllowance = 28,
            probationMonths = 3,
            excludePublicHolidaysFromSickness = true,
            displaySalaryOnEmployeeProfile = true,
            fitNoteRequiredAfterDays = 7,
            returnToWorkRequiredAfterDays = 3,
            noticePeriodUnit = "Weeks",
            noticePeriodLength = 4,
            autoDisableAccessOnLeavingDate = false
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<UpdateCompanySettingsPayload>();
        Assert.NotNull(payload);
        Assert.Equal(createdCompany.Id, payload!.CompanyId);
        Assert.Equal("Europe/London", payload.TimeZone);
        Assert.Equal(28, payload.DefaultHolidayAllowance);
        Assert.True(payload.ExcludePublicHolidaysFromSickness);
        Assert.True(payload.DisplaySalaryOnEmployeeProfile);
        Assert.Equal(7, payload.FitNoteRequiredAfterDays);
        Assert.Equal(3, payload.ReturnToWorkRequiredAfterDays);
        Assert.Equal("Weeks", payload.NoticePeriodUnit);
        Assert.Equal(4, payload.NoticePeriodLength);
        Assert.False(payload.AutoDisableAccessOnLeavingDate);
    }

    [Fact]
    public async Task Put_Company_Settings_Returns_UnprocessableEntity_When_NoticePeriodLength_Is_Not_Positive()
    {
        using var client = AuthenticatedClient(UserId);

        var createResponse = await client.PostAsJsonAsync("/api/companies", new
        {
            name = $"Settings Test {Guid.NewGuid():N}",
            addresses = new[]
            {
                new { type = "RegisteredOffice", line1 = "10 High Street", city = "London", countryCode = "GB" }
            }
        });
        createResponse.EnsureSuccessStatusCode();

        var createdCompany = await createResponse.Content.ReadFromJsonAsync<CreateCompanyPayload>();
        Assert.NotNull(createdCompany);

        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, createdCompany!.Id.ToString());

        var response = await client.PutAsJsonAsync($"/api/companies/{createdCompany.Id}/settings", new
        {
            timeZone = "UTC",
            locale = "en-GB",
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
    public async Task Put_Company_Settings_Defaults_DisplaySalaryOnEmployeeProfile_To_False_When_Omitted()
    {
        using var client = AuthenticatedClient(UserId);

        var createResponse = await client.PostAsJsonAsync("/api/companies", new
        {
            name = $"Settings Test {Guid.NewGuid():N}",
            addresses = new[]
            {
                new { type = "RegisteredOffice", line1 = "10 High Street", city = "London", countryCode = "GB" }
            }
        });
        createResponse.EnsureSuccessStatusCode();

        var createdCompany = await createResponse.Content.ReadFromJsonAsync<CreateCompanyPayload>();
        Assert.NotNull(createdCompany);

        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, createdCompany!.Id.ToString());

        var response = await client.PutAsJsonAsync($"/api/companies/{createdCompany.Id}/settings", new
        {
            timeZone = "UTC",
            locale = "en-GB",
            workingDays = 31,
            hoursPerDay = 7.5,
            leaveYearStartMonth = 1,
            defaultHolidayAllowance = 25,
            probationMonths = 6
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<UpdateCompanySettingsPayload>();
        Assert.NotNull(payload);
        Assert.False(payload!.DisplaySalaryOnEmployeeProfile);
    }

    [Fact]
    public async Task Put_Company_Settings_Persists_EmployeeNumberSettings_And_Reads_Them_Back()
    {
        using var client = AuthenticatedClient(UserId);

        var createResponse = await client.PostAsJsonAsync("/api/companies", new
        {
            name = $"Settings Test {Guid.NewGuid():N}",
            addresses = new[]
            {
                new { type = "RegisteredOffice", line1 = "10 High Street", city = "London", countryCode = "GB" }
            }
        });
        createResponse.EnsureSuccessStatusCode();

        var createdCompany = await createResponse.Content.ReadFromJsonAsync<CreateCompanyPayload>();
        Assert.NotNull(createdCompany);

        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, createdCompany!.Id.ToString());

        var response = await client.PutAsJsonAsync($"/api/companies/{createdCompany.Id}/settings", new
        {
            timeZone = "UTC",
            locale = "en-GB",
            workingDays = 31,
            hoursPerDay = 7.5,
            leaveYearStartMonth = 1,
            defaultHolidayAllowance = 25,
            probationMonths = 6,
            employeeNumberMode = "Automatic",
            employeeNumberPrefix = "EMP-",
            nextEmployeeNumber = 125,
            employeeNumberMinimumLength = 5
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<UpdateCompanySettingsPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Automatic", payload!.EmployeeNumberMode);
        Assert.Equal("EMP-", payload.EmployeeNumberPrefix);
        Assert.Equal(125, payload.NextEmployeeNumber);
        Assert.Equal(5, payload.EmployeeNumberMinimumLength);

        var getResponse = await client.GetAsync($"/api/companies/{createdCompany.Id}/settings");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var getPayload = await getResponse.Content.ReadFromJsonAsync<GetCompanySettingsPayload>();
        Assert.NotNull(getPayload);
        Assert.Equal("Automatic", getPayload!.EmployeeNumberMode);
        Assert.Equal("EMP-", getPayload.EmployeeNumberPrefix);
        Assert.Equal(125, getPayload.NextEmployeeNumber);
        Assert.Equal(5, getPayload.EmployeeNumberMinimumLength);
    }

    [Fact]
    public async Task Put_Company_Settings_Returns_UnprocessableEntity_When_NextEmployeeNumber_Is_Not_Positive()
    {
        using var client = AuthenticatedClient(UserId);

        var createResponse = await client.PostAsJsonAsync("/api/companies", new
        {
            name = $"Settings Test {Guid.NewGuid():N}",
            addresses = new[]
            {
                new { type = "RegisteredOffice", line1 = "10 High Street", city = "London", countryCode = "GB" }
            }
        });
        createResponse.EnsureSuccessStatusCode();

        var createdCompany = await createResponse.Content.ReadFromJsonAsync<CreateCompanyPayload>();
        Assert.NotNull(createdCompany);

        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, createdCompany!.Id.ToString());

        var response = await client.PutAsJsonAsync($"/api/companies/{createdCompany.Id}/settings", new
        {
            timeZone = "UTC",
            locale = "en-GB",
            workingDays = 31,
            hoursPerDay = 7.5,
            leaveYearStartMonth = 1,
            defaultHolidayAllowance = 25,
            probationMonths = 6,
            nextEmployeeNumber = 0
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_Company_Settings_Returns_UnprocessableEntity_When_EmployeeNumberMinimumLength_Is_Out_Of_Range()
    {
        using var client = AuthenticatedClient(UserId);

        var createResponse = await client.PostAsJsonAsync("/api/companies", new
        {
            name = $"Settings Test {Guid.NewGuid():N}",
            addresses = new[]
            {
                new { type = "RegisteredOffice", line1 = "10 High Street", city = "London", countryCode = "GB" }
            }
        });
        createResponse.EnsureSuccessStatusCode();

        var createdCompany = await createResponse.Content.ReadFromJsonAsync<CreateCompanyPayload>();
        Assert.NotNull(createdCompany);

        client.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, createdCompany!.Id.ToString());

        var response = await client.PutAsJsonAsync($"/api/companies/{createdCompany.Id}/settings", new
        {
            timeZone = "UTC",
            locale = "en-GB",
            workingDays = 31,
            hoursPerDay = 7.5,
            leaveYearStartMonth = 1,
            defaultHolidayAllowance = 25,
            probationMonths = 6,
            employeeNumberMinimumLength = 11
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_Company_Settings_Returns_NotFound_For_Unknown_Id()
    {
        using var client = AuthenticatedClient(UserId);

        var response = await client.PutAsJsonAsync($"/api/companies/{Guid.NewGuid()}/settings", new
        {
            timeZone = "UTC",
            locale = "en-GB",
            workingDays = 31,
            hoursPerDay = 7.5,
            leaveYearStartMonth = 1,
            defaultHolidayAllowance = 25,
            probationMonths = 6
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record CreateCompanyPayload(Guid Id);

    private sealed record UpdateCompanySettingsPayload(
        Guid CompanyId,
        string TimeZone,
        string Locale,
        string WorkingDays,
        decimal HoursPerDay,
        int LeaveYearStartMonth,
        decimal DefaultHolidayAllowance,
        int ProbationMonths,
        bool ExcludePublicHolidaysFromLeave,
        bool ExcludePublicHolidaysFromSickness,
        bool DisplaySalaryOnEmployeeProfile,
        int? FitNoteRequiredAfterDays,
        int? ReturnToWorkRequiredAfterDays,
        string NoticePeriodUnit,
        int NoticePeriodLength,
        bool AutoDisableAccessOnLeavingDate,
        string EmployeeNumberMode,
        string? EmployeeNumberPrefix,
        int NextEmployeeNumber,
        int EmployeeNumberMinimumLength,
        DateTimeOffset UpdatedAt);

    private sealed record GetCompanySettingsPayload(
        Guid CompanyId,
        string TimeZone,
        string Locale,
        int WorkingDays,
        decimal HoursPerDay,
        int LeaveYearStartMonth,
        decimal DefaultHolidayAllowance,
        int ProbationMonths,
        bool ExcludePublicHolidaysFromLeave,
        bool ExcludePublicHolidaysFromSickness,
        bool DisplaySalaryOnEmployeeProfile,
        int? FitNoteRequiredAfterDays,
        int? ReturnToWorkRequiredAfterDays,
        string PostcodeRegex,
        string TelephoneRegex,
        string MobileRegex,
        string DefaultAcknowledgementStatement,
        int AcknowledgementReminderIntervalDays,
        string NoticePeriodUnit,
        int NoticePeriodLength,
        bool AutoDisableAccessOnLeavingDate,
        string EmployeeNumberMode,
        string? EmployeeNumberPrefix,
        int NextEmployeeNumber,
        int EmployeeNumberMinimumLength,
        DateTimeOffset UpdatedAt);
}
