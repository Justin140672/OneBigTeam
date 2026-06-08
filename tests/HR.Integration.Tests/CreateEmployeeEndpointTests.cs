using System.Net;
using System.Net.Http.Json;
using HR.Modules.Employees.Domain;
using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

public class CreateEmployeeEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public CreateEmployeeEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Post_Employees_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/companies/{Guid.NewGuid()}/employees", new
        {
            firstName = "Alice",
            lastName = "Smith",
            workEmail = "alice@example.com",
            startDate = "2026-07-01"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Employees_Creates_Employee_With_Draft_Status()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "emp-user-1");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.smith.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var payload = await response.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal("Alice", payload.FirstName);
        Assert.Equal("Smith", payload.LastName);
        Assert.Equal("Draft", payload.Status);
        Assert.Null(payload.DepartmentId);
        Assert.Null(payload.PositionProfileId);
        Assert.Null(payload.ManagerId);
    }

    [Fact]
    public async Task Post_Employees_Creates_Employee_With_Department_PositionProfile_And_Manager()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "emp-user-2");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var deptResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/departments", new
        {
            companyId,
            name = $"Engineering {Guid.NewGuid():N}"
        });
        deptResponse.EnsureSuccessStatusCode();
        var dept = await deptResponse.Content.ReadFromJsonAsync<DepartmentPayload>();

        var ppResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            title = $"Developer {Guid.NewGuid():N}",
            isManagerial = false
        });
        ppResponse.EnsureSuccessStatusCode();
        var pp = await ppResponse.Content.ReadFromJsonAsync<PositionProfilePayload>();

        var managerResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName = "Jane",
            lastName = "Manager",
            workEmail = $"jane.manager.{Guid.NewGuid():N}@example.com",
            startDate = "2026-01-01"
        });
        managerResponse.EnsureSuccessStatusCode();
        var manager = await managerResponse.Content.ReadFromJsonAsync<EmployeePayload>();

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            departmentId = dept!.Id,
            positionProfileId = pp!.Id,
            managerId = manager!.Id,
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.smith.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(payload);
        Assert.Equal(dept.Id, payload!.DepartmentId);
        Assert.Equal(pp.Id, payload.PositionProfileId);
        Assert.Equal(manager.Id, payload.ManagerId);
    }

    [Fact]
    public async Task Post_Employees_Returns_Conflict_For_Duplicate_WorkEmail()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var email = $"duplicate.{Guid.NewGuid():N}@example.com";
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "emp-user-3");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var first = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName = "Alice",
            lastName = "Smith",
            workEmail = email,
            startDate = "2026-07-01"
        });
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName = "Alice",
            lastName = "Smith",
            workEmail = email,
            startDate = "2026-07-01"
        });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Post_Employees_Returns_NotFound_For_Unknown_Department()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "emp-user-4");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            departmentId = Guid.NewGuid(),
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Employees_Returns_NotFound_For_Unknown_Manager()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "emp-user-5");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            managerId = Guid.NewGuid(),
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record DepartmentPayload(Guid Id);
    private sealed record PositionProfilePayload(Guid Id);

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
        DateTimeOffset CreatedAt);
}
