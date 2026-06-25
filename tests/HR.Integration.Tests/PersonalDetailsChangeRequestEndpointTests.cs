using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class PersonalDetailsChangeRequestEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser       = Guid.Parse("11100002-0000-0000-0000-000000000001");
    private static readonly Guid SeededCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public PersonalDetailsChangeRequestEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Returns_Unauthorized_Without_Auth()
    {
        using var client = _factory.CreateClient();
        var response     = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{Guid.NewGuid()}/personal-details-change-requests",
            new { notes = "Test" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_NotFound_For_Unknown_Employee()
    {
        var userId       = Guid.NewGuid();
        using var client = AuthenticatedClient(userId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{userId}/personal-details-change-requests",
            new { companyId = SeededCompanyId, employeeId = userId, notes = "Update please" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_When_Requesting_For_Different_Employee()
    {
        // Create an employee
        using var adminClient = AdminClient();
        var employee          = await CreateEmployeeAsync(adminClient);

        // Authenticate as a different user (not the employee) and try to request a change for them
        var otherUserId  = Guid.NewGuid();
        using var client = AuthenticatedClient(otherUserId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee.Id}/personal-details-change-requests",
            new { companyId = SeededCompanyId, employeeId = employee.Id, notes = "Sneaky" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Created_With_TaskId_When_Employee_Requests_Own_Change()
    {
        using var adminClient = AdminClient();
        var employee          = await CreateEmployeeAsync(adminClient);

        // Authenticate as the employee (sub == employee.Id)
        using var client = AuthenticatedClient(employee.Id);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee.Id}/personal-details-change-requests",
            new { companyId = SeededCompanyId, employeeId = employee.Id, notes = "Please update my address." });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ChangeRequestPayload>();
        Assert.NotEqual(Guid.Empty, payload!.TaskId);
    }

    [Fact]
    public async Task Task_Appears_In_Unassigned_Tasks_After_Request()
    {
        using var adminClient = AdminClient();
        var employee          = await CreateEmployeeAsync(adminClient);

        using var selfClient  = AuthenticatedClient(employee.Id);
        var changeResp        = await selfClient.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employee.Id}/personal-details-change-requests",
            new { companyId = SeededCompanyId, employeeId = employee.Id, notes = "Unassigned task check." });
        changeResp.EnsureSuccessStatusCode();
        var changePayload = await changeResp.Content.ReadFromJsonAsync<ChangeRequestPayload>();

        // The created task has no assignee — verify it appears in unassigned tasks
        var tasksResp = await adminClient.GetAsync(
            $"/api/companies/{SeededCompanyId}/tasks/unassigned");
        Assert.Equal(HttpStatusCode.OK, tasksResp.StatusCode);

        var tasks = await tasksResp.Content.ReadFromJsonAsync<UnassignedTasksPayload>();
        Assert.Contains(tasks!.Items, t => t.Id == changePayload!.TaskId);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private HttpClient AdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        return client;
    }

    private HttpClient AuthenticatedClient(Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        return client;
    }

    private async Task<EmpPayload> CreateEmployeeAsync(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees",
            new
            {
                companyId   = SeededCompanyId,
                firstName   = "Personal",
                lastName    = "Details",
                workEmail   = $"personal.details.{Guid.NewGuid():N}@test.com",
                startDate   = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender      = "Female"
            });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<EmpPayload>())!;
    }

    private sealed record EmpPayload(Guid Id);
    private sealed record ChangeRequestPayload(Guid TaskId);
    private sealed record UnassignedTasksPayload(IReadOnlyList<UnassignedTaskItem> Items);
    private sealed record UnassignedTaskItem(Guid Id, string Title);
}
