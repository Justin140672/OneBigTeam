using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class UpdateEmployeeProfileEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid UpdEmpUser1 = new("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid UpdEmpUser2 = new("cccccccc-0000-0000-0000-000000000002");
    private static readonly Guid UpdEmpUser3 = new("cccccccc-0000-0000-0000-000000000003");

    public UpdateEmployeeProfileEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, UpdEmpUser1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, UpdEmpUser2, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, UpdEmpUser3, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Put_Employee_Profile_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/profile",
            new { firstName = "Alice", lastName = "Smith", workEmail = "alice@example.com", startDate = "2026-07-01" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Employee_Profile_Updates_Profile()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UpdEmpUser1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var created = await CreateEmployeeAsync(client, companyId, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com");

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{created.Id}/profile",
            new
            {
                companyId,
                id = created.Id,
                firstName = "Alicia",
                lastName = "Jones",
                workEmail = $"alicia.jones.{Guid.NewGuid():N}@example.com",
                personalEmail = "alicia@gmail.com",
                startDate = "2026-08-01"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<EmployeeProfilePayload>();
        Assert.NotNull(payload);
        Assert.Equal("Alicia", payload!.FirstName);
        Assert.Equal("Jones", payload.LastName);
        Assert.Equal("alicia@gmail.com", payload.PersonalEmail);
        Assert.Equal(new DateOnly(2026, 8, 1), payload.StartDate);
    }

    [Fact]
    public async Task Put_Employee_Profile_Returns_Conflict_When_Email_Already_Taken()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UpdEmpUser2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var emp1 = await CreateEmployeeAsync(client, companyId, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com");
        var emp2 = await CreateEmployeeAsync(client, companyId, "Bob", "Jones", $"bob.{Guid.NewGuid():N}@example.com");

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{emp1.Id}/profile",
            new
            {
                companyId,
                id = emp1.Id,
                firstName = "Alice",
                lastName = "Smith",
                workEmail = emp2.WorkEmail,  // already taken by emp2
                startDate = "2026-07-01"
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Put_Employee_Profile_Returns_NotFound_For_Unknown_Employee()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UpdEmpUser3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/profile",
            new
            {
                companyId,
                id = Guid.NewGuid(),
                firstName = "Alice",
                lastName = "Smith",
                workEmail = "alice@example.com",
                startDate = "2026-07-01"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<EmployeePayload> CreateEmployeeAsync(
        HttpClient client, Guid companyId, string firstName, string lastName, string workEmail)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName,
            lastName,
            workEmail,
            startDate = "2026-07-01"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EmployeePayload>())!;
    }

    private sealed record EmployeePayload(Guid Id, string WorkEmail);

    private sealed record EmployeeProfilePayload(
        Guid Id,
        Guid CompanyId,
        string FirstName,
        string LastName,
        string WorkEmail,
        string? PersonalEmail,
        DateOnly StartDate,
        string Status,
        DateTimeOffset UpdatedAt);
}
