using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
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
            startDate = "2026-07-01",
            dateOfBirth = "1990-05-20",
            nationality = "British",
            gender = "Female"
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
    public async Task Get_Employee_Returns_Forbidden_When_Route_Company_Does_Not_Match_Auth_Tenant()
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
            startDate = "2026-07-01",
            dateOfBirth = "1988-11-03",
            nationality = "British",
            gender = "Male"
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(created);

        // Authenticated as companyA but route targets companyB — middleware blocks it.
        var response = await client.GetAsync($"/api/companies/{companyB}/employees/{created!.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Employee_Includes_Department_Position_And_Manager_Names()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, GetEmpUser1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        // Create department
        var deptResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/departments", new
        {
            companyId,
            name = "Engineering"
        });
        deptResponse.EnsureSuccessStatusCode();
        var dept = await deptResponse.Content.ReadFromJsonAsync<DeptPayload>();

        // Create position profile
        var posResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId = dept!.Id,
            title = "Senior Developer"
        });
        posResponse.EnsureSuccessStatusCode();
        var pos = await posResponse.Content.ReadFromJsonAsync<PosPayload>();

        // Create manager employee
        var mgrResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName = "Jane",
            lastName = "Manager",
            workEmail = $"jane.mgr.{Guid.NewGuid():N}@example.com",
            startDate = "2025-01-01",
            dateOfBirth = "1980-06-15",
            nationality = "British",
            gender = "Female"
        });
        mgrResponse.EnsureSuccessStatusCode();
        var mgr = await mgrResponse.Content.ReadFromJsonAsync<EmployeePayload>();

        // Create employee
        var empResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.{Guid.NewGuid():N}@example.com",
            startDate = "2026-01-01",
            departmentId = dept!.Id,
            positionProfileId = pos!.Id,
            dateOfBirth = "1990-05-20",
            nationality = "British",
            gender = "Female"
        });
        empResponse.EnsureSuccessStatusCode();
        var created = await empResponse.Content.ReadFromJsonAsync<EmployeePayload>();

        // Assign manager
        await client.PutAsJsonAsync($"/api/companies/{companyId}/employees/{created!.Id}/manager", new
        {
            companyId,
            employeeId = created.Id,
            managerId = mgr!.Id
        });

        // Fetch and assert
        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(payload);
        Assert.Equal("Engineering", payload!.DepartmentName);
        Assert.Equal("Senior Developer", payload.PositionTitle);
        Assert.Equal("Jane Manager", payload.ManagerFullName);
    }

    [Fact]
    public async Task Get_Employee_Includes_DirectReportsCount_And_ReportingChain()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, GetEmpUser3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        // Department, Location, Position Profile, Employee Number and Employment Type are all
        // mandatory on employee creation — set up shared reference data once, then reuse it for
        // every employee below (the reporting-chain relationships are what's under test, not
        // these fields).
        var deptResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/departments",
            new { companyId, name = $"Dept-{Guid.NewGuid():N}" });
        deptResponse.EnsureSuccessStatusCode();
        var departmentId = (await deptResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locTypeResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/location-types",
            new { companyId, name = $"LocType-{Guid.NewGuid():N}" });
        locTypeResponse.EnsureSuccessStatusCode();
        var locationTypeId = (await locTypeResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/locations",
            new { companyId, name = $"Loc-{Guid.NewGuid():N}", locationTypeId });
        locResponse.EnsureSuccessStatusCode();
        var locationId = (await locResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var posResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles",
            new { companyId, departmentId, locationId, title = $"Role-{Guid.NewGuid():N}" });
        posResponse.EnsureSuccessStatusCode();
        var positionProfileId = (await posResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var etResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employment-types",
            new { companyId, name = $"EmpType-{Guid.NewGuid():N}" });
        etResponse.EnsureSuccessStatusCode();
        var employmentTypeId = (await etResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        async Task<EmployeePayload> CreateEmployeeAsync(string firstName, string lastName)
        {
            var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
            {
                companyId,
                firstName,
                lastName,
                workEmail = $"{firstName}.{lastName}.{Guid.NewGuid():N}@example.com".ToLowerInvariant(),
                startDate = "2025-01-01",
                dateOfBirth = "1985-01-01",
                nationality = "British",
                gender = "Female",
                employeeNumber = $"EMP-{Guid.NewGuid():N}",
                employmentTypeId,
                departmentId,
                locationId,
                positionProfileId
            });
            createResponse.EnsureSuccessStatusCode();
            return (await createResponse.Content.ReadFromJsonAsync<EmployeePayload>())!;
        }

        async Task AssignManagerAsync(Guid employeeId, Guid managerId)
        {
            var assignResponse = await client.PutAsJsonAsync(
                $"/api/companies/{companyId}/employees/{employeeId}/manager",
                new { companyId, employeeId, managerId });
            assignResponse.EnsureSuccessStatusCode();
        }

        var ceo      = await CreateEmployeeAsync("Carla", "Ceo");
        var manager  = await CreateEmployeeAsync("Dan", "Director");
        var employee = await CreateEmployeeAsync("Alice", "Smith");
        var peer     = await CreateEmployeeAsync("Bob", "Jones");

        await AssignManagerAsync(manager.Id, ceo.Id);
        await AssignManagerAsync(employee.Id, manager.Id);
        await AssignManagerAsync(peer.Id, manager.Id);

        var employeeResponse = await client.GetAsync($"/api/companies/{companyId}/employees/{employee.Id}");
        Assert.Equal(HttpStatusCode.OK, employeeResponse.StatusCode);
        var employeePayload = await employeeResponse.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(employeePayload);
        Assert.NotNull(employeePayload!.ReportingChain);
        Assert.Equal(2, employeePayload.ReportingChain!.Count);
        Assert.Equal(ceo.Id, employeePayload.ReportingChain[0].EmployeeId);
        Assert.Equal("Carla Ceo", employeePayload.ReportingChain[0].Name);
        Assert.Equal(manager.Id, employeePayload.ReportingChain[1].EmployeeId);
        Assert.Equal("Dan Director", employeePayload.ReportingChain[1].Name);

        var managerResponse = await client.GetAsync($"/api/companies/{companyId}/employees/{manager.Id}");
        Assert.Equal(HttpStatusCode.OK, managerResponse.StatusCode);
        var managerPayload = await managerResponse.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(managerPayload);
        Assert.Equal(2, managerPayload!.DirectReportsCount);
        Assert.Single(managerPayload.ReportingChain!);
        Assert.Equal(ceo.Id, managerPayload.ReportingChain![0].EmployeeId);
    }

    private sealed record EmployeePayload(
        Guid Id,
        Guid CompanyId,
        Guid? DepartmentId,
        string? DepartmentName,
        Guid? PositionProfileId,
        string? PositionTitle,
        Guid? ManagerId,
        string? ManagerFullName,
        int DirectReportsCount,
        List<ReportingChainItemPayload>? ReportingChain,
        string FirstName,
        string LastName,
        string WorkEmail,
        string? PersonalEmail,
        DateOnly StartDate,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed record ReportingChainItemPayload(Guid EmployeeId, string Name, string? JobTitle);

    private sealed record DeptPayload(Guid Id, string Name);
    private sealed record PosPayload(Guid Id, string Title);
    private sealed record IdPayload(Guid Id);
}
