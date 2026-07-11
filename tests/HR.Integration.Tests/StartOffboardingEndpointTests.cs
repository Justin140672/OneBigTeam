using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class StartOffboardingEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("cc000003-0000-0000-0000-000000000001");

    public StartOffboardingEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private HttpClient AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private async Task<Guid> CreateEmployeeAsync(HttpClient client, Guid companyId, string firstName = "Jamie", string lastName = "Smith")
    {
        var resp = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName,
            lastName,
            workEmail = $"{firstName.ToLower()}.{lastName.ToLower()}.{Guid.NewGuid():N}@offboardtest.example",
            startDate = "2026-01-01",
            dateOfBirth = "1990-01-01",
            nationality = "British",
            gender = "Male"
        });
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
    }

    [Fact]
    public async Task Post_StartOffboarding_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding/start",
            new { companyId, employeeId, lastWorkingDay = "2026-08-01" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_StartOffboarding_Starts_Offboarding_For_Existing_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var employeeId = await CreateEmployeeAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding/start",
            new { companyId, employeeId, lastWorkingDay = "2026-08-01", notes = "Resigned." });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var payload = await response.Content.ReadFromJsonAsync<OffboardingPlanPayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal(employeeId, payload.EmployeeId);
        Assert.Equal(new DateOnly(2026, 8, 1), payload.LastWorkingDay);
        Assert.Equal("InProgress", payload.Status);
        Assert.Equal("Resigned.", payload.Notes);
        Assert.NotEmpty(payload.GeneratedTaskIds);
    }

    [Fact]
    public async Task Post_StartOffboarding_Returns_NotFound_When_Employee_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding/start",
            new { companyId, employeeId, lastWorkingDay = "2026-08-01" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_StartOffboarding_Returns_Conflict_When_Plan_Already_Exists()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var employeeId = await CreateEmployeeAsync(client, companyId);

        var firstResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding/start",
            new { companyId, employeeId, lastWorkingDay = "2026-08-01" });
        firstResponse.EnsureSuccessStatusCode();

        var secondResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding/start",
            new { companyId, employeeId, lastWorkingDay = "2026-08-15" });

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Post_StartOffboarding_Returns_ValidationError_When_LastWorkingDay_Is_Missing()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);
        var employeeId = await CreateEmployeeAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding/start",
            new { companyId, employeeId });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record OffboardingPlanPayload(
        Guid Id,
        Guid CompanyId,
        Guid EmployeeId,
        DateOnly LastWorkingDay,
        string Status,
        string? Notes,
        IReadOnlyList<Guid> GeneratedTaskIds,
        DateTimeOffset CreatedAt);
}
