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

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName = "Alice",
            lastName = "Smith",
            workEmail = $"alice.smith.{Guid.NewGuid():N}@example.com",
            startDate = "2026-07-01",
            dateOfBirth = "1990-05-20",
            nationality = "British",
            gender = "Female"
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
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User2.ToString());
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
            title = $"Developer {Guid.NewGuid():N}"
        });
        ppResponse.EnsureSuccessStatusCode();
        var pp = await ppResponse.Content.ReadFromJsonAsync<PositionProfilePayload>();

        var managerResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName = "Jane",
            lastName = "Manager",
            workEmail = $"jane.manager.{Guid.NewGuid():N}@example.com",
            startDate = "2026-01-01",
            dateOfBirth = "1985-03-10",
            nationality = "British",
            gender = "Female"
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

        var first = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName = "Alice",
            lastName = "Smith",
            workEmail = email,
            startDate = "2026-07-01",
            dateOfBirth = "1990-05-20",
            nationality = "British",
            gender = "Female"
        });
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName = "Alice",
            lastName = "Smith",
            workEmail = email,
            startDate = "2026-07-01",
            dateOfBirth = "1990-05-20",
            nationality = "British",
            gender = "Female"
        });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Post_Employees_Returns_NotFound_For_Unknown_Department()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User4.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            departmentId = Guid.NewGuid(),
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

        var locationId = await CreateLocationAsync(client, companyId);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            locationId,
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

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            locationId = Guid.NewGuid(),
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

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
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
