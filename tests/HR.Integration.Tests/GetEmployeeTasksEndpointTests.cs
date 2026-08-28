using HR.Modules.Tasks.Contracts;
using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetEmployeeTasksEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid SeededCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AdminUser = new("eeeeeeee-0000-0000-0000-000000000002");

    public GetEmployeeTasksEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    // ── Auth ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_EmployeeTasks_Returns_Unauthorized_When_No_Auth_Header()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{Guid.NewGuid()}/tasks");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_EmployeeTasks_Returns_Ok_For_Authenticated_User_With_No_Role()
    {
        // Endpoint requires only the baseline "role:employee" policy (any authenticated
        // employee), not employee:manage, so employees can view their own tasks without
        // needing management permissions. IAM-07: the caller must still pass resource-level
        // authorization, so this requests the caller's own tasks (self-access) rather than an
        // arbitrary employeeId — an unrelated employeeId is covered by the authorization-matrix
        // tests below.
        var unprivilegedUser = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, unprivilegedUser, SystemRoles.Employee);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, unprivilegedUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, unprivilegedUser, SystemRoles.Employee, SeededCompanyId);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{unprivilegedUser}/tasks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Happy path ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_EmployeeTasks_Returns_Empty_When_Employee_Has_No_Tasks()
    {
        using var client = await AuthenticatedClient();

        var employee = await CreateEmployeeAsync(client, "Solo", "Employee");

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee.Id}/tasks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_EmployeeTasks_Returns_Tasks_Assigned_To_Employee()
    {
        using var client = await AuthenticatedClient();

        var employee = await CreateEmployeeAsync(client, "Alice", "Smith");
        var other    = await CreateEmployeeAsync(client, "Bob",   "Jones");

        await CreateTaskAsync("Alice task A", employee.Id);
        await CreateTaskAsync("Alice task B", employee.Id);
        await CreateTaskAsync("Bob task",     other.Id);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee.Id}/tasks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.Equal(2, payload!.Items.Count);
        Assert.All(payload.Items, item => Assert.Equal(employee.Id, item.AssignedEmployeeId));
    }

    [Fact]
    public async Task Get_EmployeeTasks_Filters_By_Status_When_Provided()
    {
        using var client = await AuthenticatedClient();

        var employee = await CreateEmployeeAsync(client, "Carol", "Davis");

        await CreateTaskAsync("Open task A", employee.Id, priority: TaskPriority.Low);
        await CreateTaskAsync("Open task B", employee.Id, priority: TaskPriority.High);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee.Id}/tasks?status=Open");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.True(payload!.Items.Count >= 2);
        Assert.All(payload.Items, item => Assert.Equal("Open", item.Status));
    }

    // ── IAM-07: resource-ownership authorization matrix ───────────────────────
    // Unlike GetTask, this check runs directly in the Endpoint (the target employeeId is known
    // from the route, no DB lookup needed first) — see Endpoint.cs. Mirrors
    // CompleteTaskAuthorizationTests's matrix for the same underlying TasksResourceAuthorizer.

    [Fact]
    public async Task Get_EmployeeTasks_Returns_Ok_For_Self()
    {
        var employee = await CreateEmployeeAsCallerAsync();

        using var client = await AuthenticatedAsAsync(employee);
        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee}/tasks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_EmployeeTasks_Returns_Ok_For_The_Employees_Direct_Manager()
    {
        var manager = await CreateEmployeeAsCallerAsync();
        var report = await CreateEmployeeAsCallerAsync();

        using (var setupClient = await AuthenticatedAsAsync(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, report, manager);
        }

        using var client = await AuthenticatedAsAsync(manager);
        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{report}/tasks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_EmployeeTasks_Returns_Ok_For_Skip_Level_Manager_In_Three_Level_Hierarchy()
    {
        var seniorManager = await CreateEmployeeAsCallerAsync(); // C
        var manager = await CreateEmployeeAsCallerAsync();       // B
        var report = await CreateEmployeeAsCallerAsync();        // A

        using (var setupClient = await AuthenticatedAsAsync(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, report, manager);
            await AssignManagerAsync(setupClient, manager, seniorManager);
        }

        using var client = await AuthenticatedAsAsync(seniorManager);
        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{report}/tasks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_EmployeeTasks_Returns_Forbidden_For_Unrelated_Peer_Employee()
    {
        var peer = await CreateEmployeeAsCallerAsync();
        var target = await CreateEmployeeAsCallerAsync();

        using var client = await AuthenticatedAsAsync(peer);
        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{target}/tasks");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_EmployeeTasks_Returns_Forbidden_For_Manager_Of_A_Different_Team()
    {
        var manager = await CreateEmployeeAsCallerAsync();
        var otherManager = await CreateEmployeeAsCallerAsync();
        var report = await CreateEmployeeAsCallerAsync();

        using (var setupClient = await AuthenticatedAsAsync(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, report, manager);
        }

        using var client = await AuthenticatedAsAsync(otherManager);
        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{report}/tasks");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_EmployeeTasks_Returns_Ok_For_HrAdministrator_Even_When_Not_Self_Or_Manager()
    {
        var target = await CreateEmployeeAsCallerAsync();

        using var client = await AuthenticatedAsAsync(Guid.NewGuid(), hrAdministrator: true);
        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{target}/tasks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_EmployeeTasks_Returns_Forbidden_When_Route_Company_Does_Not_Match_Auth_Tenant()
    {
        var target = await CreateEmployeeAsCallerAsync();
        var otherCompanyId = Guid.NewGuid();

        using var client = await AuthenticatedAsAsync(Guid.NewGuid());
        var response = await client.GetAsync(
            $"/api/companies/{otherCompanyId}/employees/{target}/tasks");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<HttpClient> AuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUser, SystemRoles.HrAdministrator, SeededCompanyId);
        return client;
    }

    private async Task<HttpClient> AuthenticatedAsAsync(Guid userId, bool hrAdministrator = false)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee, SeededCompanyId);

        if (hrAdministrator)
            await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.HrAdministrator, SeededCompanyId);

        return client;
    }

    private static async Task AssignManagerAsync(HttpClient client, Guid employeeId, Guid managerId)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employeeId}/manager",
            new { companyId = SeededCompanyId, id = employeeId, managerId });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Creates a real employee record whose id doubles as the identity user id used for
    /// TestAuthHandler.UserHeader, so the returned id can act as a caller in
    /// AuthenticatedAsAsync — mirrors CompleteTaskAuthorizationTests.CreateEmployeeAsync.
    /// </summary>
    private async Task<Guid> CreateEmployeeAsCallerAsync()
    {
        using var setupClient = await AuthenticatedAsAsync(Guid.NewGuid(), hrAdministrator: true);
        var employee = await CreateEmployeeAsync(setupClient, "Test", $"Employee-{Guid.NewGuid():N}"[..20]);
        return employee.Id;
    }

    private async Task<EmployeePayload> CreateEmployeeAsync(HttpClient client, string firstName, string lastName)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees",
            new
            {
                companyId = SeededCompanyId,
                firstName,
                lastName,
                workEmail = $"{firstName.ToLower()}.{lastName.ToLower()}.{Guid.NewGuid():N}@example.com",
                startDate = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender = "Male",
                // Max 50 chars (CreateEmployeeValidator) — a full firstName-lastName-guid
                // combination can exceed that, so use a short, still-unique suffix instead.
                employeeNumber = $"EN-{Guid.NewGuid():N}"[..20],
                employmentTypeId = Guid.Parse("40000000-0000-0000-0000-000000000001"),
                departmentId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                locationId = Guid.Parse("70000000-0000-0000-0000-000000000001"),
                positionProfileId = Guid.Parse("20000000-0000-0000-0000-000000000002")
            });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EmployeePayload>())!;
    }

    private Task CreateTaskAsync(
        string title,
        Guid assignedEmployeeId,
        TaskPriority priority = TaskPriority.Medium) =>
        TaskSeeder.SeedAsync(_factory, SeededCompanyId, title, priority: priority, assignedEmployeeId: assignedEmployeeId);

    private sealed record EmployeePayload(Guid Id);

    private sealed record ListPayload(IReadOnlyList<EmployeeTaskItem> Items);

    private sealed record EmployeeTaskItem(
        Guid Id,
        Guid CompanyId,
        string Title,
        string? Description,
        string Status,
        string Priority,
        string Source,
        string? DueDate,
        Guid? AssignedEmployeeId,
        Guid? AssignedUserId,
        Guid CreatedBy,
        Guid? CompletedBy,
        DateTimeOffset? CompletedAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
