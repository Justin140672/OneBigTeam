using System.Net;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Tasks.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// SEC-003: only the task's assignee, the assignee's direct manager, or an HR Administrator may
/// complete a task via POST /api/companies/{companyId}/tasks/{id}/complete. Endpoint-level
/// Policies("role:employee") only proves tenant membership, not resource ownership, so these
/// tests exercise the resource-ownership authorization check performed in CompleteTaskHandler.
/// </summary>
[Collection("Integration")]
public class CompleteTaskAuthorizationTests(ApiWebApplicationFactory factory)
{
    private static readonly Guid SeededCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // ── Anonymous ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Complete_Task_Returns_Unauthorized_When_No_Auth_Header()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/companies/{SeededCompanyId}/tasks/{Guid.NewGuid()}/complete",
            EmptyJson());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Unrelated peer ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Complete_Task_Returns_Forbidden_For_Unrelated_Peer_Employee()
    {
        var assignee = await CreateEmployeeAsync();
        var peer = await CreateEmployeeAsync();

        var taskId = await CreateTaskAsync("Assignee's task", assignedEmployeeId: assignee);

        using var client = await AuthenticatedClient(peer);

        var response = await client.PostAsync(
            $"/api/companies/{SeededCompanyId}/tasks/{taskId}/complete",
            EmptyJson());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Assignee ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Complete_Task_Returns_Ok_For_The_Assignee()
    {
        var assignee = await CreateEmployeeAsync();

        var taskId = await CreateTaskAsync("Assignee's task", assignedEmployeeId: assignee);

        using var client = await AuthenticatedClient(assignee);

        var response = await client.PostAsync(
            $"/api/companies/{SeededCompanyId}/tasks/{taskId}/complete",
            EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<TaskPayload>();
        Assert.Equal("Completed", payload!.Status);
    }

    // ── Direct manager ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Complete_Task_Returns_Ok_For_The_Assignees_Direct_Manager()
    {
        var manager = await CreateEmployeeAsync();
        var report = await CreateEmployeeAsync();

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, report, manager);
        }

        var taskId = await CreateTaskAsync("Report's task", assignedEmployeeId: report);

        using var client = await AuthenticatedClient(manager);

        var response = await client.PostAsync(
            $"/api/companies/{SeededCompanyId}/tasks/{taskId}/complete",
            EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Complete_Task_Returns_Forbidden_For_Manager_Of_A_Different_Team()
    {
        var manager = await CreateEmployeeAsync();
        var otherManager = await CreateEmployeeAsync();
        var report = await CreateEmployeeAsync();

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, report, manager);
        }

        var taskId = await CreateTaskAsync("Report's task", assignedEmployeeId: report);

        using var client = await AuthenticatedClient(otherManager);

        var response = await client.PostAsync(
            $"/api/companies/{SeededCompanyId}/tasks/{taskId}/complete",
            EmptyJson());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Skip-level manager (three-level hierarchy) ────────────────────────────

    [Fact]
    public async Task Complete_Task_Returns_Ok_For_Skip_Level_Manager_In_Three_Level_Hierarchy()
    {
        var seniorManager = await CreateEmployeeAsync(); // C
        var manager = await CreateEmployeeAsync();       // B
        var report = await CreateEmployeeAsync();        // A

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, report, manager);
            await AssignManagerAsync(setupClient, manager, seniorManager);
        }

        var taskId = await CreateTaskAsync("Report's task", assignedEmployeeId: report);

        using var client = await AuthenticatedClient(seniorManager);

        var response = await client.PostAsync(
            $"/api/companies/{SeededCompanyId}/tasks/{taskId}/complete",
            EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Company Administrator (not HR Administrator) ──────────────────────────

    [Fact]
    public async Task Complete_Task_Returns_Forbidden_For_CompanyAdministrator_Who_Is_Not_Assignee_Or_Manager()
    {
        var assignee = await CreateEmployeeAsync();
        var companyAdmin = Guid.NewGuid();

        var taskId = await CreateTaskAsync("Assignee's task", assignedEmployeeId: assignee);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, companyAdmin.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(factory, companyAdmin, SystemRoles.CompanyAdministrator, SeededCompanyId);

        var response = await client.PostAsync(
            $"/api/companies/{SeededCompanyId}/tasks/{taskId}/complete",
            EmptyJson());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── AssignedUserId-only task ───────────────────────────────────────────────

    [Fact]
    public async Task Complete_Task_Returns_Ok_For_Manager_Of_AssignedUserId_Only_Task()
    {
        var manager = await CreateEmployeeAsync();
        var report = await CreateEmployeeAsync();

        using (var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true))
        {
            await AssignManagerAsync(setupClient, report, manager);
        }

        var taskId = await TaskSeeder.SeedAsync(
            factory, SeededCompanyId, "User-assigned task", assignedUserId: report);

        using var client = await AuthenticatedClient(manager);

        var response = await client.PostAsync(
            $"/api/companies/{SeededCompanyId}/tasks/{taskId}/complete",
            EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── HR administrator override ─────────────────────────────────────────────

    [Fact]
    public async Task Complete_Task_Returns_Ok_For_HrAdministrator_Even_When_Not_Assignee_Or_Manager()
    {
        var assignee = await CreateEmployeeAsync();
        var taskId = await CreateTaskAsync("Assignee's task", assignedEmployeeId: assignee);

        using var client = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var response = await client.PostAsync(
            $"/api/companies/{SeededCompanyId}/tasks/{taskId}/complete",
            EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Idempotency guard vs. authorization ordering ─────────────────────────

    [Fact]
    public async Task Complete_Task_Returns_Forbidden_For_Unauthorized_Caller_Even_When_Already_Completed()
    {
        var assignee = await CreateEmployeeAsync();
        var peer = await CreateEmployeeAsync();

        var taskId = await CreateTaskAsync(
            "Already completed",
            assignedEmployeeId: assignee,
            status: TaskItemStatus.Completed);

        using var client = await AuthenticatedClient(peer);

        var response = await client.PostAsync(
            $"/api/companies/{SeededCompanyId}/tasks/{taskId}/complete",
            EmptyJson());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Cancelled task, authorized caller (existing behavior unchanged) ──────

    [Fact]
    public async Task Complete_Task_Returns_Conflict_For_Cancelled_Task_When_Caller_Is_Authorized_Assignee()
    {
        var assignee = await CreateEmployeeAsync();

        var taskId = await CreateTaskAsync(
            "Cancelled task",
            assignedEmployeeId: assignee,
            status: TaskItemStatus.Cancelled);

        using var client = await AuthenticatedClient(assignee);

        var response = await client.PostAsync(
            $"/api/companies/{SeededCompanyId}/tasks/{taskId}/complete",
            EmptyJson());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ── Unassigned tasks ───────────────────────────────────────────────────────

    [Fact]
    public async Task Complete_Task_Returns_Forbidden_For_Unassigned_Task_When_Caller_Is_Not_HrAdministrator()
    {
        var caller = await CreateEmployeeAsync();

        var taskId = await CreateTaskAsync("Unassigned task");

        using var client = await AuthenticatedClient(caller);

        var response = await client.PostAsync(
            $"/api/companies/{SeededCompanyId}/tasks/{taskId}/complete",
            EmptyJson());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Complete_Task_Returns_Ok_For_Unassigned_Task_When_Caller_Is_HrAdministrator()
    {
        var taskId = await CreateTaskAsync("Unassigned task");

        using var client = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

        var response = await client.PostAsync(
            $"/api/companies/{SeededCompanyId}/tasks/{taskId}/complete",
            EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<HttpClient> AuthenticatedClient(Guid userId, bool hrAdministrator = false)
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
    /// Creates a real employee record via the employees API and returns its id. Note that in
    /// this system an employee's id doubles as the identity user id for the linked account (see
    /// GetMyEmployeeHandler's `e.Id == userId` lookup) — so this same id is used both as the
    /// task's AssignedEmployeeId and as the TestAuthHandler.UserHeader value when acting "as"
    /// that employee.
    /// </summary>
    private async Task<Guid> CreateEmployeeAsync()
    {
        using var setupClient = await AuthenticatedClient(Guid.NewGuid(), hrAdministrator: true);

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

    private Task<Guid> CreateTaskAsync(
        string title,
        Guid? assignedEmployeeId = null,
        TaskItemStatus status = TaskItemStatus.Open) =>
        TaskSeeder.SeedAsync(
            factory, SeededCompanyId, title,
            assignedEmployeeId: assignedEmployeeId,
            status: status);

    private static StringContent EmptyJson() =>
        new("{}", Encoding.UTF8, "application/json");

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
