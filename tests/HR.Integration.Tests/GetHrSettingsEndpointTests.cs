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

    private async Task<HttpClient> AuthenticatedClient(Guid tenantId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, UserId, SystemRoles.Employee, tenantId);
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
        using var client = await AuthenticatedClient(UserId);

        var createResponse = await client.PostAsJsonAsync("/api/companies", new
        {
            name = $"Hr Settings Test {Guid.NewGuid():N}",
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

        var response = await client.GetAsync($"/api/companies/{createdCompany.Id}/hr-settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<GetHrSettingsPayload>();
        Assert.NotNull(payload);
        Assert.Equal(createdCompany.Id, payload!.CompanyId);
        Assert.Equal("Automatic", payload.EmployeeNumberMode);
        Assert.Equal(1, payload.NextEmployeeNumber);
    }

    [Fact]
    public async Task Get_Hr_Settings_Returns_NotFound_For_Unknown_Id()
    {
        using var client = await AuthenticatedClient(UserId);

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/hr-settings");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record CreateCompanyPayload(Guid Id);

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
        DateTimeOffset UpdatedAt);
}
