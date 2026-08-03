using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// Tests the self-service read endpoints: GetMyEmployee, GetMyPersonalDetails,
/// GetMyContactDetails. All three use the 'sub' claim (X-Test-User) as the
/// employee identity and look up by employee.Id == userId.
/// </summary>
[Collection("Integration")]
public class SelfServiceReadEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser       = Guid.Parse("11100007-0000-0000-0000-000000000001");
    private static readonly Guid SeededCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public SelfServiceReadEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    // ── GetMyEmployee ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyEmployee_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response     = await client.GetAsync($"/api/companies/{SeededCompanyId}/employees/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMyEmployee_Returns_NotFound_When_No_Employee_Linked_To_User()
    {
        using var client = SelfClient(Guid.NewGuid()); // user with no employee record
        var response     = await client.GetAsync($"/api/companies/{SeededCompanyId}/employees/me");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMyEmployee_Returns_Employee_When_User_Matches_Employee_Id()
    {
        using var adminClient = AdminClient();
        var employee          = await CreateEmployeeAsync(adminClient, "Self", "Service");

        // employee.Id is the sub claim — GetMyEmployee looks up by e.Id == userId
        using var selfClient  = SelfClient(employee.Id);
        var response          = await selfClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<MyEmployeePayload>();
        Assert.Equal(employee.Id, payload!.EmployeeId);
        Assert.Equal("Self",      payload.FirstName);
        Assert.Equal("Service",   payload.LastName);
    }

    // ── GetMyPersonalDetails ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyPersonalDetails_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response     = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/me/personal-details");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMyPersonalDetails_Returns_NotFound_When_No_Employee_Linked()
    {
        using var client = SelfClient(Guid.NewGuid());
        var response     = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/me/personal-details");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMyPersonalDetails_Returns_Personal_Data_For_Employee()
    {
        using var adminClient = AdminClient();
        var employee          = await CreateEmployeeAsync(adminClient, "Jane", "Doe",
            dateOfBirth: "1992-03-15", nationality: "French", gender: "Female");

        using var selfClient  = SelfClient(employee.Id);
        var response          = await selfClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/me/personal-details");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PersonalDetailsPayload>();
        Assert.Equal(employee.Id,          payload!.EmployeeId);
        Assert.Equal("Jane",               payload.FirstName);
        Assert.Equal("Doe",                payload.LastName);
        Assert.Equal("French",             payload.Nationality);
        Assert.Equal("Female",             payload.Gender);
        Assert.Equal(new DateOnly(1992, 3, 15), payload.DateOfBirth);
    }

    // ── GetMyContactDetails ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyContactDetails_Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response     = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/me/contact-details");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMyContactDetails_Returns_NotFound_When_No_Employee_Linked()
    {
        using var client = SelfClient(Guid.NewGuid());
        var response     = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/me/contact-details");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMyContactDetails_Returns_Contact_Info_For_Employee()
    {
        using var adminClient = AdminClient();
        var workEmail         = $"contact.test.{Guid.NewGuid():N}@test.com";
        var employee          = await CreateEmployeeAsync(adminClient, "Contact", "Test",
            workEmail: workEmail);

        using var selfClient  = SelfClient(employee.Id);
        var response          = await selfClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/me/contact-details");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ContactDetailsPayload>();
        Assert.Equal(workEmail, payload!.WorkEmail);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private HttpClient AdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        return client;
    }

    private HttpClient SelfClient(Guid userId)
    {
        TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee).GetAwaiter().GetResult();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        return client;
    }

    private async Task<EmpPayload> CreateEmployeeAsync(
        HttpClient client,
        string firstName,
        string lastName,
        string dateOfBirth  = "1990-01-01",
        string nationality  = "British",
        string gender       = "Male",
        string? workEmail   = null)
    {
        var email = workEmail ?? $"{firstName.ToLower()}.{lastName.ToLower()}.{Guid.NewGuid():N}@test.com";
        var resp  = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees",
            new
            {
                companyId   = SeededCompanyId,
                firstName,
                lastName,
                workEmail   = email,
                startDate   = "2026-01-01",
                dateOfBirth,
                nationality,
                gender,
                employeeNumber    = $"{firstName}-{lastName}-{Guid.NewGuid():N}",
                employmentTypeId  = Guid.Parse("40000000-0000-0000-0000-000000000001"),
                departmentId      = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                locationId        = Guid.Parse("70000000-0000-0000-0000-000000000001"),
                positionProfileId = Guid.Parse("20000000-0000-0000-0000-000000000002")
            });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<EmpPayload>())!;
    }

    private sealed record EmpPayload(Guid Id);
    private sealed record MyEmployeePayload(Guid EmployeeId, string FirstName, string LastName, string? WorkingDaysOverride, decimal? HoursPerDayOverride);
    private sealed record PersonalDetailsPayload(Guid EmployeeId, string FirstName, string LastName, string? PreferredName, DateOnly? DateOfBirth, string? Nationality, string? Gender);
    private sealed record ContactDetailsPayload(string WorkEmail, string? PersonalEmail, string? PhoneNumber);
}
