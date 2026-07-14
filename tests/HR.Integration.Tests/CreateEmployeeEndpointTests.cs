using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class CreateEmployeeEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    // Pre-seeded user IDs that have the HrAdministrator role.
    private static readonly Guid User1 = new("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid User2 = new("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid User3 = new("aaaaaaaa-0000-0000-0000-000000000003");
    private static readonly Guid User4 = new("aaaaaaaa-0000-0000-0000-000000000004");
    private static readonly Guid User5 = new("aaaaaaaa-0000-0000-0000-000000000005");
    private static readonly Guid User6 = new("aaaaaaaa-0000-0000-0000-000000000006");
    private static readonly Guid User7 = new("aaaaaaaa-0000-0000-0000-000000000007");

    public CreateEmployeeEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        // Seed HrAdministrator role for each test user so the permission check passes.
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User2, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User3, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User4, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User5, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User6, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User7, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    private static async Task<Guid> CreateLocationAsync(HttpClient client, Guid companyId, string name = "Head Office")
    {
        var locationTypeResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/location-types", new
        {
            companyId,
            name = "Office"
        });
        locationTypeResponse.EnsureSuccessStatusCode();
        var locationType = await locationTypeResponse.Content.ReadFromJsonAsync<IdPayload>();

        var locationResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/locations", new
        {
            companyId,
            name,
            locationTypeId = locationType!.Id
        });
        locationResponse.EnsureSuccessStatusCode();
        var location = await locationResponse.Content.ReadFromJsonAsync<IdPayload>();
        return location!.Id;
    }

    private sealed record IdPayload(Guid Id);

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
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        // Department, Location, Position Profile, Employment Type and Employee Number are all
        // mandatory on employee creation — seed the minimum real reference data required for any
        // employee to be created at all (see EmployeeReferenceDataSeeder for why).
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Alice", "Smith", $"alice.smith.{Guid.NewGuid():N}@example.com"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var payload = await response.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal(companyId, payload.CompanyId);
        Assert.Equal("Alice", payload.FirstName);
        Assert.Equal("Smith", payload.LastName);
        Assert.Equal("Draft", payload.Status);
        Assert.Equal(refData.DepartmentId, payload.DepartmentId);
        Assert.Equal(refData.LocationId, payload.LocationId);
        Assert.Equal(refData.PositionProfileId, payload.PositionProfileId);
        Assert.Null(payload.ManagerId);
    }

    [Fact]
    public async Task Post_Employees_Creates_Employee_With_Department_PositionProfile_And_Manager()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        // Base reference data used for the manager (whose own department/position-profile isn't
        // under test here); a distinct department + position profile are created afterwards to
        // verify the employee-under-test picks up its own explicitly-assigned values.
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var managerResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Jane", "Manager", $"jane.manager.{Guid.NewGuid():N}@example.com",
                startDate: new DateOnly(2026, 1, 1), dateOfBirth: new DateOnly(1985, 3, 10)));
        managerResponse.EnsureSuccessStatusCode();
        var manager = await managerResponse.Content.ReadFromJsonAsync<EmployeePayload>();

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
            title = $"Developer {Guid.NewGuid():N}"
        });
        ppResponse.EnsureSuccessStatusCode();
        var pp = await ppResponse.Content.ReadFromJsonAsync<PositionProfilePayload>();

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            departmentId = dept!.Id,
            locationId = refData.LocationId,
            positionProfileId = pp!.Id,
            employmentTypeId = refData.EmploymentTypeId,
            employeeNumber = $"EMP-{Guid.NewGuid():N}",
            managerId = manager!.Id,
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.smith.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01",
            dateOfBirth = "1990-05-20",
            nationality = "British",
            gender = "Female"
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
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User3.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var first = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(companyId, refData, "Alice", "Smith", email));
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(companyId, refData, "Alice", "Smith", email));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Post_Employees_Returns_NotFound_For_Unknown_Department()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User4.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        // Location/PositionProfile/EmploymentType are real (seeded) so the NotFound is
        // attributable specifically to the unknown DepartmentId, not some other missing lookup.
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            departmentId = Guid.NewGuid(),
            locationId = refData.LocationId,
            positionProfileId = refData.PositionProfileId,
            employmentTypeId = refData.EmploymentTypeId,
            employeeNumber = $"EMP-{Guid.NewGuid():N}",
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01",
            dateOfBirth = "1990-05-20",
            nationality = "British",
            gender = "Female"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Employees_Creates_Employee_With_Location()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User6.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);
        var locationId = await CreateLocationAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            departmentId = refData.DepartmentId,
            locationId,
            positionProfileId = refData.PositionProfileId,
            employmentTypeId = refData.EmploymentTypeId,
            employeeNumber = $"EMP-{Guid.NewGuid():N}",
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.smith.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01",
            dateOfBirth = "1990-05-20",
            nationality = "British",
            gender = "Female"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<EmployeePayload>();
        Assert.NotNull(payload);
        Assert.Equal(locationId, payload!.LocationId);
    }

    [Fact]
    public async Task Post_Employees_Returns_NotFound_For_Unknown_Location()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User7.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            departmentId = refData.DepartmentId,
            locationId = Guid.NewGuid(),
            positionProfileId = refData.PositionProfileId,
            employmentTypeId = refData.EmploymentTypeId,
            employeeNumber = $"EMP-{Guid.NewGuid():N}",
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01",
            dateOfBirth = "1990-05-20",
            nationality = "British",
            gender = "Female"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Employees_Returns_NotFound_For_Unknown_Manager()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User5.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            departmentId = refData.DepartmentId,
            locationId = refData.LocationId,
            positionProfileId = refData.PositionProfileId,
            employmentTypeId = refData.EmploymentTypeId,
            employeeNumber = $"EMP-{Guid.NewGuid():N}",
            managerId = Guid.NewGuid(),
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01",
            dateOfBirth = "1990-05-20",
            nationality = "British",
            gender = "Female"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record DepartmentPayload(Guid Id);
    private sealed record PositionProfilePayload(Guid Id);

    private sealed record EmployeePayload(
        Guid Id,
        Guid CompanyId,
        Guid? DepartmentId,
        Guid? LocationId,
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
