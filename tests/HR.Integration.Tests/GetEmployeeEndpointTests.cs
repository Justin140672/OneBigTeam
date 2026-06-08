using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class GetEmployeeEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid GetEmpUser1 = new("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid GetEmpUser2 = new("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid GetEmpUser3 = new("bbbbbbbb-0000-0000-0000-000000000003");

    public GetEmployeeEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, GetEmpUser1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, GetEmpUser2, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, GetEmpUser3, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Get_Employee_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Employee_Returns_Employee_For_Authenticated_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, GetEmpUser1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01"
        });
        createResponse.EnsureSuccessStatusCode();

        var created = await createResponse.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(created);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(payload);
        Assert.Equal(created.Id, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal("Alice", payload.FirstName);
        Assert.Equal("Smith", payload.LastName);
        Assert.Equal("Draft", payload.Status);
    }

    [Fact]
    public async Task Get_Employee_Returns_NotFound_For_Unknown_Id()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, GetEmpUser2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Employee_Returns_NotFound_When_Employee_Belongs_To_Different_Company()
    {
        using var client = _factory.CreateClient();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        // Create employee under company A
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, GetEmpUser3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyA.ToString());

        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyA}/employees", new
        {
            companyId = companyA,
            firstName = "Bob",
            lastName = "Jones",
            workEmail = $"bob.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01"
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(created);

        // Request the employee using company B's scope
        var response = await client.GetAsync($"/api/companies/{companyB}/employees/{created!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record EmployeePayload(
        Guid Id,
        Guid CompanyId,
        Guid? DepartmentId,
        Guid? PositionProfileId,
        Guid? ManagerId,
        string FirstName,
        string LastName,
        string WorkEmail,
        string? PersonalEmail,
        DateOnly StartDate,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
