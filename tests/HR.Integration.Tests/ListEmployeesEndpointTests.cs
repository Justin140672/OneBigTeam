using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class ListEmployeesEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid ListEmpUser1 = new("eeeeeeee-0000-0000-0000-000000000001");
    private static readonly Guid ListEmpUser2 = new("eeeeeeee-0000-0000-0000-000000000002");
    private static readonly Guid ListEmpUser3 = new("eeeeeeee-0000-0000-0000-000000000003");
    private static readonly Guid ListEmpUser4 = new("eeeeeeee-0000-0000-0000-000000000004");
    private static readonly Guid ListEmpUser5 = new("eeeeeeee-0000-0000-0000-000000000005");

    public ListEmployeesEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, ListEmpUser1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ListEmpUser1, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, ListEmpUser2, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ListEmpUser2, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, ListEmpUser3, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ListEmpUser3, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, ListEmpUser4, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ListEmpUser4, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, ListEmpUser5, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ListEmpUser5, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
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
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ListEmpUser1.ToString());
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
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ListEmpUser2.ToString());
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
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ListEmpUser3.ToString());
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
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ListEmpUser4.ToString());
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
    public async Task Get_Employees_Returns_Forbidden_When_Route_Company_Does_Not_Match_Auth_Tenant()
    {
        using var client = _factory.CreateClient();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, ListEmpUser5.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyA.ToString());
        await CreateEmployeeAsync(client, companyA, "Alice", "Smith", $"alice.{Guid.NewGuid():N}@example.com");

        // Authenticated as companyA but route targets companyB — middleware blocks it.
        var response = await client.GetAsync($"/api/companies/{companyB}/employees");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<(Guid DepartmentId, Guid LocationId, Guid PositionProfileId, Guid EmploymentTypeId)> CreateReferenceDataAsync(
        HttpClient client, Guid companyId)
    {
        var deptResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/departments",
            new { companyId, name = $"Dept {Guid.NewGuid():N}" });
        deptResp.EnsureSuccessStatusCode();
        var departmentId = (await deptResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locTypeResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/location-types",
            new { companyId, name = $"LocType {Guid.NewGuid():N}" });
        locTypeResp.EnsureSuccessStatusCode();
        var locationTypeId = (await locTypeResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/locations",
            new { companyId, name = $"Loc {Guid.NewGuid():N}", locationTypeId });
        locResp.EnsureSuccessStatusCode();
        var locationId = (await locResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var leavePolicyResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"RefLeavePolicy {Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false });
        leavePolicyResp.EnsureSuccessStatusCode();
        var defaultLeavePolicyId = (await leavePolicyResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var posResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles",
            new { companyId, departmentId, locationId, title = $"Title {Guid.NewGuid():N}", defaultLeavePolicyId });
        posResp.EnsureSuccessStatusCode();
        var positionProfileId = (await posResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var empTypeResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employment-types",
            new { companyId, name = $"EmpType {Guid.NewGuid():N}" });
        empTypeResp.EnsureSuccessStatusCode();
        var employmentTypeId = (await empTypeResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        return (departmentId, locationId, positionProfileId, employmentTypeId);
    }

    private static async Task CreateEmployeeAsync(HttpClient client, Guid companyId, string firstName, string lastName, string workEmail)
    {
        var refData = await CreateReferenceDataAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName,
            lastName,
            workEmail,
            startDate = "2026-07-01",
            dateOfBirth = "1990-01-01",
            nationality = "British",
            gender = "Male",
            employeeNumber = $"EMP-{Guid.NewGuid():N}",
            employmentTypeId = refData.EmploymentTypeId,
            departmentId = refData.DepartmentId,
            locationId = refData.LocationId,
            positionProfileId = refData.PositionProfileId
        });
        response.EnsureSuccessStatusCode();
    }

    private sealed record IdPayload(Guid Id);

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
        string Status);
}
