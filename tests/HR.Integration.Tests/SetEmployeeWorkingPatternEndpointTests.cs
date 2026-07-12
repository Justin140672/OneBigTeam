using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class SetEmployeeWorkingPatternEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser       = Guid.Parse("11100006-0000-0000-0000-000000000001");
    private static readonly Guid SeededCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public SetEmployeeWorkingPatternEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response     = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{Guid.NewGuid()}/working-pattern",
            new { hoursPerDayOverride = 7.5m });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_Without_Employee_Manage_Role()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{Guid.NewGuid()}/working-pattern",
            new { hoursPerDayOverride = 7.5m });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_NotFound_For_Unknown_Employee()
    {
        using var client = AdminClient();
        var unknownId    = Guid.NewGuid();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{unknownId}/working-pattern",
            new { companyId = SeededCompanyId, employeeId = unknownId, hoursPerDayOverride = 7.5m });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_OK_And_Persists_HoursPerDay_Override()
    {
        using var client = AdminClient();
        var employee     = await CreateEmployeeAsync(client);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee.Id}/working-pattern",
            new { companyId = SeededCompanyId, employeeId = employee.Id, hoursPerDayOverride = 7.0m });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<WorkingPatternPayload>();
        Assert.Equal(7.0m,      payload!.HoursPerDayOverride);
        Assert.Null(            payload.WorkingDaysOverride);
        Assert.Equal(employee.Id, payload.EmployeeId);
    }

    [Fact]
    public async Task Working_Pattern_Override_Is_Reflected_In_GetMyEmployee()
    {
        using var client = AdminClient();
        var employee     = await CreateEmployeeAsync(client);

        await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee.Id}/working-pattern",
            new { companyId = SeededCompanyId, employeeId = employee.Id, hoursPerDayOverride = 6.5m });

        // GetMyEmployee uses employee.Id as the userId (sub claim)
        using var selfClient = SelfClient(employee.Id);
        var meResp = await selfClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/me");
        Assert.Equal(HttpStatusCode.OK, meResp.StatusCode);

        var mePayload = await meResp.Content.ReadFromJsonAsync<MyEmployeePayload>();
        Assert.Equal(6.5m, mePayload!.HoursPerDayOverride);
    }

    [Fact]
    public async Task Clearing_Override_Returns_Null_Values()
    {
        using var client = AdminClient();
        var employee     = await CreateEmployeeAsync(client);

        // First set an override
        await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee.Id}/working-pattern",
            new { companyId = SeededCompanyId, employeeId = employee.Id, hoursPerDayOverride = 7.0m });

        // Then clear it by sending nulls
        var clearResp = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee.Id}/working-pattern",
            new { companyId = SeededCompanyId, employeeId = employee.Id,
                  workingDaysOverride = (object?)null, hoursPerDayOverride = (decimal?)null });

        Assert.Equal(HttpStatusCode.OK, clearResp.StatusCode);
        var payload = await clearResp.Content.ReadFromJsonAsync<WorkingPatternPayload>();
        Assert.Null(payload!.HoursPerDayOverride);
        Assert.Null(payload.WorkingDaysOverride);
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
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        return client;
    }

    private async Task<EmpPayload> CreateEmployeeAsync(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees",
            new
            {
                companyId   = SeededCompanyId,
                firstName   = "Working",
                lastName    = "Pattern",
                workEmail   = $"working.pattern.{Guid.NewGuid():N}@test.com",
                startDate   = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender      = "Male",
                employeeNumber    = $"WP-{Guid.NewGuid():N}",
                employmentTypeId  = Guid.Parse("40000000-0000-0000-0000-000000000001"),
                departmentId      = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                locationId        = Guid.Parse("70000000-0000-0000-0000-000000000001"),
                positionProfileId = Guid.Parse("20000000-0000-0000-0000-000000000002")
            });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<EmpPayload>())!;
    }

    private sealed record EmpPayload(Guid Id);
    private sealed record WorkingPatternPayload(Guid EmployeeId, string? WorkingDaysOverride, decimal? HoursPerDayOverride);
    private sealed record MyEmployeePayload(Guid EmployeeId, string? WorkingDaysOverride, decimal? HoursPerDayOverride);
}
