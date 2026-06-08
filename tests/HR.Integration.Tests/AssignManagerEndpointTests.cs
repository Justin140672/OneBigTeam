using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

public class AssignManagerEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public AssignManagerEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Put_Manager_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/manager",
            new { managerId = (Guid?)null });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Manager_Assigns_Manager()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "mgr-user-1");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var manager = await CreateEmployeeAsync(client, companyId, "Jane", "Manager", $"jane.{Guid.NewGuid():N}@example.com");
        var employee = await CreateEmployeeAsync(client, companyId, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com");

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/manager",
            new { companyId, id = employee.Id, managerId = manager.Id });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AssignManagerPayload>();
        Assert.NotNull(payload);
        Assert.Equal(manager.Id, payload!.ManagerId);
        Assert.Equal("Jane Manager", payload.ManagerFullName);
    }

    [Fact]
    public async Task Put_Manager_Removes_Manager_When_Null()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "mgr-user-2");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var manager = await CreateEmployeeAsync(client, companyId, "Jane", "Manager", $"jane2.{Guid.NewGuid():N}@example.com");
        var employee = await CreateEmployeeAsync(client, companyId, "Alice", "Smith", $"alice2.{Guid.NewGuid():N}@example.com");

        // Assign first
        await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/manager",
            new { companyId, id = employee.Id, managerId = manager.Id });

        // Then remove
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee.Id}/manager",
            new { companyId, id = employee.Id, managerId = (Guid?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AssignManagerPayload>();
        Assert.NotNull(payload);
        Assert.Null(payload!.ManagerId);
        Assert.Null(payload.ManagerFullName);
    }

    [Fact]
    public async Task Put_Manager_Returns_Conflict_For_Direct_Circular_Assignment()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "mgr-user-3");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var empA = await CreateEmployeeAsync(client, companyId, "Alice", "Smith", $"alice3.{Guid.NewGuid():N}@example.com");
        var empB = await CreateEmployeeAsync(client, companyId, "Bob", "Jones", $"bob3.{Guid.NewGuid():N}@example.com");

        // B reports to A
        await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{empB.Id}/manager",
            new { companyId, id = empB.Id, managerId = empA.Id });

        // Try to assign B as manager of A — circular
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{empA.Id}/manager",
            new { companyId, id = empA.Id, managerId = empB.Id });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Put_Manager_Returns_Conflict_For_Deep_Circular_Assignment()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "mgr-user-4");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var empA = await CreateEmployeeAsync(client, companyId, "Alice", "Smith", $"a4.{Guid.NewGuid():N}@example.com");
        var empB = await CreateEmployeeAsync(client, companyId, "Bob", "Jones", $"b4.{Guid.NewGuid():N}@example.com");
        var empC = await CreateEmployeeAsync(client, companyId, "Carol", "White", $"c4.{Guid.NewGuid():N}@example.com");

        // B → A, C → B
        await client.PutAsJsonAsync($"/api/companies/{companyId}/employees/{empB.Id}/manager",
            new { companyId, id = empB.Id, managerId = empA.Id });
        await client.PutAsJsonAsync($"/api/companies/{companyId}/employees/{empC.Id}/manager",
            new { companyId, id = empC.Id, managerId = empB.Id });

        // Try A → C (would create A→B→C→A cycle)
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{empA.Id}/manager",
            new { companyId, id = empA.Id, managerId = empC.Id });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Put_Manager_Returns_NotFound_For_Unknown_Employee()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "mgr-user-5");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/manager",
            new { companyId, id = Guid.NewGuid(), managerId = (Guid?)null });

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

    private sealed record EmployeePayload(Guid Id);

    private sealed record AssignManagerPayload(
        Guid Id,
        Guid CompanyId,
        Guid? ManagerId,
        string? ManagerFullName,
        DateTimeOffset UpdatedAt);
}
