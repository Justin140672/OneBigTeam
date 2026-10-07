using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetProposedLastWorkingDayEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid AdminUser = new("fadeaa01-9000-0000-0000-000000000001");
    private static readonly Guid EmployeeOnlyUser = new("fadeaa01-9000-0000-0000-000000000002");

    private static readonly DateOnly LeavingSaturday = NextSaturdayOnOrAfter(
        DateOnly.FromDateTime(DateTime.UtcNow).AddDays(400));

    public GetProposedLastWorkingDayEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, EmployeeOnlyUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private static DateOnly NextSaturdayOnOrAfter(DateOnly date)
    {
        var daysUntilSaturday = ((int)DayOfWeek.Saturday - (int)date.DayOfWeek + 7) % 7;
        return date.AddDays(daysUntilSaturday);
    }

    private async Task<(HttpClient Client, Guid CompanyId)> AdminClientAsync()
    {
        var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUser, SystemRoles.HrAdministrator, companyId);
        return (client, companyId);
    }

    private static async Task<Guid> CreateEmployeeAsync(HttpClient client, Guid companyId)
    {
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Proposed", "LastDay", $"proposed.{Guid.NewGuid():N}@example.com"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private static async Task CreatePublicHolidayAsync(HttpClient client, Guid companyId, DateOnly date)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/public-holidays",
            new { companyId, date = date.ToString("yyyy-MM-dd"), name = "Test Holiday", countryCode = "GB" });
        response.EnsureSuccessStatusCode();
    }

    private static async Task SetWorkingDaysAsync(HttpClient client, Guid companyId, Guid employeeId, int workingDays)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/working-pattern",
            new { companyId, employeeId, workingDaysOverride = workingDays, hoursPerDayOverride = 7.5m });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<DateOnly> GetProposedAsync(
        HttpClient client, Guid companyId, Guid employeeId, DateOnly leavingDate)
    {
        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/proposed-last-working-day?leavingDate={leavingDate:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ProposedPayload>();
        return payload!.ProposedLastWorkingDay;
    }

    [Fact]
    public async Task Get_ProposedLastWorkingDay_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/proposed-last-working-day?leavingDate={LeavingSaturday:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_ProposedLastWorkingDay_Returns_Forbidden_For_Employee_Role_Only_User()
    {
        var (adminClient, companyId) = await AdminClientAsync();
        using var _ = adminClient;
        var employeeId = await CreateEmployeeAsync(adminClient, companyId);

        using var employeeClient = _factory.CreateClient();
        employeeClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, EmployeeOnlyUser.ToString());
        employeeClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, EmployeeOnlyUser, SystemRoles.Employee, companyId);

        var response = await employeeClient.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/proposed-last-working-day?leavingDate={LeavingSaturday:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_ProposedLastWorkingDay_Returns_Forbidden_When_Route_Company_Does_Not_Match_Auth_Tenant()
    {
        var (client, companyId) = await AdminClientAsync();
        using var _ = client;
        var employeeId = await CreateEmployeeAsync(client, companyId);
        var otherCompanyId = Guid.NewGuid();

        var response = await client.GetAsync(
            $"/api/companies/{otherCompanyId}/employees/{employeeId}/proposed-last-working-day?leavingDate={LeavingSaturday:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_ProposedLastWorkingDay_Returns_NotFound_For_Unknown_Employee()
    {
        var (client, companyId) = await AdminClientAsync();
        using var _ = client;

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/proposed-last-working-day?leavingDate={LeavingSaturday:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?leavingDate=0001-01-01")]
    [InlineData("?leavingDate=not-a-date")]
    public async Task Get_ProposedLastWorkingDay_Returns_UnprocessableEntity_For_Missing_Or_Invalid_LeavingDate(string query)
    {
        var (client, companyId) = await AdminClientAsync();
        using var _ = client;
        var employeeId = await CreateEmployeeAsync(client, companyId);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/proposed-last-working-day{query}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Get_ProposedLastWorkingDay_Returns_Friday_For_Saturday_Leaving_Date()
    {
        var (client, companyId) = await AdminClientAsync();
        using var _ = client;
        var employeeId = await CreateEmployeeAsync(client, companyId);

        var proposed = await GetProposedAsync(client, companyId, employeeId, LeavingSaturday);

        Assert.Equal(LeavingSaturday.AddDays(-1), proposed);
        Assert.Equal(DayOfWeek.Friday, proposed.DayOfWeek);
    }

    [Fact]
    public async Task Get_ProposedLastWorkingDay_Returns_Same_Date_When_Leaving_Date_Is_Working_Day()
    {
        var (client, companyId) = await AdminClientAsync();
        using var _ = client;
        var employeeId = await CreateEmployeeAsync(client, companyId);
        var leavingFriday = LeavingSaturday.AddDays(-1);

        var proposed = await GetProposedAsync(client, companyId, employeeId, leavingFriday);

        Assert.Equal(leavingFriday, proposed);
    }

    [Fact]
    public async Task Get_ProposedLastWorkingDay_Returns_Thursday_When_Friday_Is_Public_Holiday()
    {
        var (client, companyId) = await AdminClientAsync();
        using var _ = client;
        var employeeId = await CreateEmployeeAsync(client, companyId);
        var friday = LeavingSaturday.AddDays(-1);
        await CreatePublicHolidayAsync(client, companyId, friday);

        var proposed = await GetProposedAsync(client, companyId, employeeId, LeavingSaturday);

        Assert.Equal(friday.AddDays(-1), proposed);
        Assert.Equal(DayOfWeek.Thursday, proposed.DayOfWeek);
    }

    [Fact]
    public async Task Get_ProposedLastWorkingDay_Ignores_Public_Holiday_Of_Another_Company()
    {
        var (client, companyId) = await AdminClientAsync();
        using var _ = client;
        var employeeId = await CreateEmployeeAsync(client, companyId);
        var friday = LeavingSaturday.AddDays(-1);
        var (otherClient, otherCompanyId) = await AdminClientAsync();
        using var __ = otherClient;
        await CreatePublicHolidayAsync(otherClient, otherCompanyId, friday);

        var proposed = await GetProposedAsync(client, companyId, employeeId, LeavingSaturday);

        Assert.Equal(friday, proposed);
    }

    [Fact]
    public async Task Get_ProposedLastWorkingDay_Uses_Part_Time_Working_Pattern_Of_Employee()
    {
        var (client, companyId) = await AdminClientAsync();
        using var _ = client;
        var employeeId = await CreateEmployeeAsync(client, companyId);
        const int mondayTuesdayWednesday = 1 | 2 | 4;
        await SetWorkingDaysAsync(client, companyId, employeeId, mondayTuesdayWednesday);

        var proposed = await GetProposedAsync(client, companyId, employeeId, LeavingSaturday);

        Assert.Equal(LeavingSaturday.AddDays(-3), proposed);
        Assert.Equal(DayOfWeek.Wednesday, proposed.DayOfWeek);
    }

    [Fact]
    public async Task Get_ProposedLastWorkingDay_Combines_Part_Time_Pattern_With_Public_Holiday()
    {
        var (client, companyId) = await AdminClientAsync();
        using var _ = client;
        var employeeId = await CreateEmployeeAsync(client, companyId);
        const int tuesdayThursday = 2 | 8;
        await SetWorkingDaysAsync(client, companyId, employeeId, tuesdayThursday);
        var thursday = LeavingSaturday.AddDays(-2);
        await CreatePublicHolidayAsync(client, companyId, thursday);

        var proposed = await GetProposedAsync(client, companyId, employeeId, LeavingSaturday);

        Assert.Equal(thursday.AddDays(-2), proposed);
        Assert.Equal(DayOfWeek.Tuesday, proposed.DayOfWeek);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record ProposedPayload(DateOnly ProposedLastWorkingDay);
}
