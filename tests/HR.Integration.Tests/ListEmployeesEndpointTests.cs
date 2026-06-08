using System.Net;
using System.Net.Http.Json;
using HR.Modules.Employees.Domain;
using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

public class ListEmployeesEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public ListEmployeesEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_Employees_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/employees");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Employees_Returns_Empty_Page_When_No_Employees()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "list-emp-user-1");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.GetAsync($"/api/companies/{companyId}/employees");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Equal(0, payload!.TotalCount);
        Assert.Empty(payload.Items);
    }

    [Fact]
    public async Task Get_Employees_Returns_Created_Employees()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "list-emp-user-2");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        await CreateEmployeeAsync(client, companyId, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com");
        await CreateEmployeeAsync(client, companyId, "Bob", "Jones", $"bob.{Guid.NewGuid():N}@example.com");

        var response = await client.GetAsync($"/api/companies/{companyId}/employees");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.TotalCount);
        Assert.Equal(2, payload.Items.Count);
        Assert.Equal(1, payload.PageNumber);
        Assert.Equal(1, payload.TotalPages);
    }

    [Fact]
    public async Task Get_Employees_Filters_By_Search()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "list-emp-user-3");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        await CreateEmployeeAsync(client, companyId, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com");
        await CreateEmployeeAsync(client, companyId, "Bob", "Jones", $"bob.{Guid.NewGuid():N}@example.com");

        var response = await client.GetAsync($"/api/companies/{companyId}/employees?search=alice");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.TotalCount);
        Assert.Equal("Alice", payload.Items[0].FirstName);
    }

    [Fact]
    public async Task Get_Employees_Returns_Paged_Results()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "list-emp-user-4");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        for (var i = 0; i < 5; i++)
        {
            await CreateEmployeeAsync(client, companyId, "Employee", $"Z{i:00}", $"emp{i}.{Guid.NewGuid():N}@example.com");
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/employees?pageNumber=1&pageSize=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Equal(5, payload!.TotalCount);
        Assert.Equal(3, payload.TotalPages);
        Assert.Equal(2, payload.Items.Count);
        Assert.Equal(1, payload.PageNumber);
        Assert.Equal(2, payload.PageSize);
    }

    [Fact]
    public async Task Get_Employees_Does_Not_Return_Employees_From_Other_Companies()
    {
        using var client = _factory.CreateClient();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "list-emp-user-5");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyA.ToString());
        await CreateEmployeeAsync(client, companyA, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com");

        var response = await client.GetAsync($"/api/companies/{companyB}/employees");

        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Equal(0, payload!.TotalCount);
    }

    private static async Task CreateEmployeeAsync(HttpClient client, Guid companyId, string firstName, string lastName, string workEmail)
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
    }

    private sealed record ListPayload(
        IReadOnlyList<EmployeeItem> Items,
        int TotalCount,
        int PageNumber,
        int PageSize,
        int TotalPages);

    private sealed record EmployeeItem(
        Guid Id,
        Guid CompanyId,
        Guid? DepartmentId,
        string? DepartmentName,
        Guid? PositionProfileId,
        string? PositionProfileTitle,
        Guid? ManagerId,
        string? ManagerFullName,
        string FirstName,
        string LastName,
        string WorkEmail,
        EmploymentStatus Status);
}
