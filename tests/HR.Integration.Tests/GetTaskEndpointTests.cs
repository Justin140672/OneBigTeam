using HR.Modules.Tasks.Contracts;
using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetTaskEndpointTests(ApiWebApplicationFactory factory)
{
    private static readonly Guid SeededCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.NewGuid();

    // ── Auth ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_Task_Returns_Unauthorized_When_No_Auth_Header()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/tasks/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Not found ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_Task_Returns_NotFound_When_Task_Does_Not_Exist()
    {
        using var client = await AuthenticatedClient();

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/tasks/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Task_Returns_Forbidden_When_Route_Company_Does_Not_Match_Auth_Tenant()
    {
        var taskId = await TaskSeeder.SeedAsync(factory, SeededCompanyId, "Private task", priority: TaskPriority.Low);

        // Authenticated as SeededCompanyId but route targets a different company — middleware blocks it.
        using var client = await AuthenticatedClient();
        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/tasks/{taskId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Happy path ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_Task_Returns_200_With_Full_Payload()
    {
        // IAM-07: only the assignee, their manager, or an HR Administrator may view a task —
        // assign it to the caller (UserId) so this remains an authorized happy-path request; a
        // separate assignee is exercised by the authorization-matrix tests below.
        var assignedEmployee = UserId;
        var taskId = await TaskSeeder.SeedAsync(
            factory, SeededCompanyId,
            title: "Schedule probation review",
            description: "Book 1-to-1 with line manager",
            priority: TaskPriority.High,
            source: TaskSource.Probation,
            dueDate: new DateOnly(2026, 9, 1),
            assignedEmployeeId: assignedEmployee,
            createdBy: UserId);

        using var client = await AuthenticatedClient();
        var response = await client.GetAsync($"/api/companies/{SeededCompanyId}/tasks/{taskId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<TaskPayload>();
        Assert.NotNull(payload);
        Assert.Equal(taskId, payload!.Id);
        Assert.Equal(SeededCompanyId, payload.CompanyId);
        Assert.Equal("Schedule probation review", payload.Title);
        Assert.Equal("Book 1-to-1 with line manager", payload.Description);
        Assert.Equal("Open", payload.Status);
        Assert.Equal("High", payload.Priority);
        Assert.Equal("Probation", payload.Source);
        Assert.Equal("2026-09-01", payload.DueDate);
        Assert.Equal(assignedEmployee, payload.AssignedEmployeeId);
        Assert.Equal(UserId, payload.CreatedBy);
        Assert.Null(payload.CompletedBy);
        Assert.Null(payload.CompletedAt);
    }

    // ── IAM-07: resource-ownership authorization matrix ───────────────────────
    // GetTask requires more than the baseline "role:employee" policy — the caller must also be
    // the task's assignee, a manager anywhere in the assignee's reporting hierarchy, or an HR
    // Administrator. Mirrors CompleteTaskAuthorizationTests's matrix for the same underlying
    // TasksResourceAuthorizer.

    [Fact]
    public async Task Get_Task_Returns_Ok_For_The_Assignee()
    {
        var assignee = await CreateEmployeeAsync();
        var taskId = await TaskSeeder.SeedAsync(factory, SeededCompanyId, "Assignee's task", assignedEmployeeId: assignee);

        using var client = await AuthenticatedAsAsync(assignee);
        var response = await client.GetAsync($"/api/companies/{SeededCompanyId}/tasks/{taskId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_Task_Returns_Ok_For_The_Assignees_Direct_Manager()
    {
        var manager = await CreateEmployeeAsync();
        var report = await CreateEmployeeAsync();

        using (var setupClient = await AuthenticatedAsAsync(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, report, manager);
        }

        var taskId = await TaskSeeder.SeedAsync(factory, SeededCompanyId, "Report's task", assignedEmployeeId: report);

        using var client = await AuthenticatedAsAsync(manager);
        var response = await client.GetAsync($"/api/companies/{SeededCompanyId}/tasks/{taskId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_Task_Returns_Ok_For_Skip_Level_Manager_In_Three_Level_Hierarchy()
    {
        var seniorManager = await CreateEmployeeAsync(); // C
        var manager = await CreateEmployeeAsync();       // B
        var report = await CreateEmployeeAsync();        // A

        using (var setupClient = await AuthenticatedAsAsync(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, report, manager);
            await AssignManagerAsync(setupClient, manager, seniorManager);
        }

        var taskId = await TaskSeeder.SeedAsync(factory, SeededCompanyId, "Report's task", assignedEmployeeId: report);

        using var client = await AuthenticatedAsAsync(seniorManager);
        var response = await client.GetAsync($"/api/companies/{SeededCompanyId}/tasks/{taskId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_Task_Returns_Forbidden_For_Unrelated_Peer_Employee()
    {
        var assignee = await CreateEmployeeAsync();
        var peer = await CreateEmployeeAsync();

        var taskId = await TaskSeeder.SeedAsync(factory, SeededCompanyId, "Assignee's task", assignedEmployeeId: assignee);

        using var client = await AuthenticatedAsAsync(peer);
        var response = await client.GetAsync($"/api/companies/{SeededCompanyId}/tasks/{taskId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Task_Returns_Forbidden_For_Manager_Of_A_Different_Team()
    {
        var manager = await CreateEmployeeAsync();
        var otherManager = await CreateEmployeeAsync();
        var report = await CreateEmployeeAsync();

        using (var setupClient = await AuthenticatedAsAsync(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, report, manager);
        }

        var taskId = await TaskSeeder.SeedAsync(factory, SeededCompanyId, "Report's task", assignedEmployeeId: report);

        using var client = await AuthenticatedAsAsync(otherManager);
        var response = await client.GetAsync($"/api/companies/{SeededCompanyId}/tasks/{taskId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Task_Returns_Ok_For_HrAdministrator_Even_When_Not_Assignee_Or_Manager()
    {
        var assignee = await CreateEmployeeAsync();
        var taskId = await TaskSeeder.SeedAsync(factory, SeededCompanyId, "Assignee's task", assignedEmployeeId: assignee);

        using var client = await AuthenticatedAsAsync(Guid.NewGuid(), hrAdministrator: true);
        var response = await client.GetAsync($"/api/companies/{SeededCompanyId}/tasks/{taskId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<HttpClient> AuthenticatedClient()
    {
        TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.Employee).GetAwaiter().GetResult();

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.Employee, SeededCompanyId);
        return client;
    }

    private async Task<HttpClient> AuthenticatedAsAsync(Guid userId, bool hrAdministrator = false)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.Employee, SeededCompanyId);

        if (hrAdministrator)
            await TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.HrAdministrator, SeededCompanyId);

        return client;
    }

    /// <summary>
    /// Creates a real employee record via the employees API and returns its id, which doubles as
    /// the identity user id for TestAuthHandler.UserHeader — mirrors
    /// CompleteTaskAuthorizationTests.CreateEmployeeAsync.
    /// </summary>
    private async Task<Guid> CreateEmployeeAsync()
    {
        using var setupClient = await AuthenticatedAsAsync(Guid.NewGuid(), hrAdministrator: true);

        var firstName = "Test";
        var unique = Guid.NewGuid().ToString("N")[..12];
        var lastName = $"Employee-{unique}";

        var response = await setupClient.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees",
            new
            {
                companyId = SeededCompanyId,
                firstName,
                lastName,
                workEmail = $"{lastName.ToLower()}@example.com",
                startDate = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender = "Male",
                employeeNumber = $"EN-{unique}",
                employmentTypeId = Guid.Parse("40000000-0000-0000-0000-000000000001"),
                departmentId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                locationId = Guid.Parse("70000000-0000-0000-0000-000000000001"),
                positionProfileId = Guid.Parse("20000000-0000-0000-0000-000000000002")
            });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<EmployeePayload>();
        return payload!.Id;
    }

    private static async Task AssignManagerAsync(HttpClient client, Guid employeeId, Guid managerId)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employeeId}/manager",
            new { companyId = SeededCompanyId, id = employeeId, managerId });
        response.EnsureSuccessStatusCode();
    }

    private sealed record EmployeePayload(Guid Id);

    private sealed record TaskPayload(
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
