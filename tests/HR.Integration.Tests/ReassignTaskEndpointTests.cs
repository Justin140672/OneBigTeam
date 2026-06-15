using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class ReassignTaskEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid SeededCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AdminUser = new("eeeeeeee-0000-0000-0000-000000000003");

    public ReassignTaskEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    // ── Auth ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reassign_Task_Returns_Unauthorized_When_No_Auth_Header()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/tasks/{Guid.NewGuid()}/assignee",
            new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reassign_Task_Returns_Forbidden_When_Caller_Has_No_Role()
    {
        var unprivilegedUser = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, unprivilegedUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/tasks/{Guid.NewGuid()}/assignee",
            new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Not found ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reassign_Task_Returns_NotFound_When_Task_Does_Not_Exist()
    {
        using var client = AuthenticatedClient();

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/tasks/{Guid.NewGuid()}/assignee",
            new { assignedEmployeeId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Happy path ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reassign_Task_Updates_AssignedEmployeeId()
    {
        using var client = AuthenticatedClient();

        var employee = await CreateEmployeeAsync(client, "Dave", "Miller");
        var taskId   = await CreateTaskAsync(client, "Task to reassign");

        var newEmployee = await CreateEmployeeAsync(client, "Eve", "Wilson");

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/tasks/{taskId}/assignee",
            new { assignedEmployeeId = newEmployee.Id });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<TaskPayload>();
        Assert.Equal(newEmployee.Id, payload!.AssignedEmployeeId);
    }

    [Fact]
    public async Task Reassign_Task_Clears_Assignment_When_Null()
    {
        using var client = AuthenticatedClient();

        var employee = await CreateEmployeeAsync(client, "Frank", "Brown");
        var taskId   = await CreateTaskAsync(client, "Task to unassign", assignedEmployeeId: employee.Id);

        var response = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/tasks/{taskId}/assignee",
            new { assignedEmployeeId = (Guid?)null, assignedUserId = (Guid?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<TaskPayload>();
        Assert.Null(payload!.AssignedEmployeeId);
        Assert.Null(payload.AssignedUserId);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private HttpClient AuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        return client;
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
                startDate = "2026-01-01"
            });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EmployeePayload>())!;
    }

    private async Task<Guid> CreateTaskAsync(
        HttpClient client,
        string title,
        Guid? assignedEmployeeId = null,
        string priority = "Medium",
        string source = "Manual")
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/tasks",
            new
            {
                companyId = SeededCompanyId,
                title,
                priority,
                source,
                assignedEmployeeId
            });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TaskPayload>();
        return payload!.Id;
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
