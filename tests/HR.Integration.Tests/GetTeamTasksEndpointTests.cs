using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Integration.Tests;

public class GetTeamTasksEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid SeededCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AdminUser = new("eeeeeeee-0000-0000-0000-000000000001");

    public GetTeamTasksEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    // ── Auth ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_TeamTasks_Returns_Unauthorized_When_No_Auth_Header()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{Guid.NewGuid()}/team-tasks");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Happy path ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_TeamTasks_Returns_Empty_When_Manager_Has_No_Direct_Reports()
    {
        using var client = AuthenticatedClient();

        var manager = await CreateEmployeeAsync(client, "Solo", "Manager");

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{manager.Id}/team-tasks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_TeamTasks_Returns_Tasks_Assigned_To_Direct_Reports()
    {
        using var client = AuthenticatedClient();

        var manager = await CreateEmployeeAsync(client, "Alice", "Manager");
        var report1 = await CreateEmployeeAsync(client, "Bob",   "Reporter");
        var report2 = await CreateEmployeeAsync(client, "Carol", "Reporter");

        await AssignManagerAsync(client, report1.Id, manager.Id);
        await AssignManagerAsync(client, report2.Id, manager.Id);

        await CreateTaskAsync("Bob's task",   report1.Id);
        await CreateTaskAsync("Carol's task", report2.Id);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{manager.Id}/team-tasks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.Equal(2, payload!.Items.Count);
        Assert.Contains(payload.Items, t => t.Title == "Bob's task");
        Assert.Contains(payload.Items, t => t.Title == "Carol's task");
    }

    [Fact]
    public async Task Get_TeamTasks_Does_Not_Return_Tasks_Assigned_To_Other_Employees()
    {
        using var client = AuthenticatedClient();

        var manager   = await CreateEmployeeAsync(client, "Dave",  "Manager");
        var report    = await CreateEmployeeAsync(client, "Eve",   "Reporter");
        var outsider  = await CreateEmployeeAsync(client, "Frank", "Outsider");

        await AssignManagerAsync(client, report.Id, manager.Id);

        await CreateTaskAsync("Team task",    report.Id);
        await CreateTaskAsync("Outside task", outsider.Id);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{manager.Id}/team-tasks");

        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.Single(payload!.Items);
        Assert.Equal("Team task", payload.Items[0].Title);
    }

    [Fact]
    public async Task Get_TeamTasks_Filters_By_Status_When_Provided()
    {
        using var client = AuthenticatedClient();

        var manager = await CreateEmployeeAsync(client, "Grace", "Manager");
        var report  = await CreateEmployeeAsync(client, "Hank",  "Reporter");

        await AssignManagerAsync(client, report.Id, manager.Id);

        await CreateTaskAsync("Open task A", report.Id, priority: TaskPriority.Low);
        await CreateTaskAsync("Open task B", report.Id, priority: TaskPriority.High);

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/employees/{manager.Id}/team-tasks?status=Open");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.True(payload!.Items.Count >= 2);
        Assert.All(payload.Items, item => Assert.Equal("Open", item.Status));
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
                startDate = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender = "Male",
                employeeNumber = $"{firstName}-{lastName}-{Guid.NewGuid():N}",
                employmentTypeId = Guid.Parse("40000000-0000-0000-0000-000000000001"),
                departmentId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                locationId = Guid.Parse("70000000-0000-0000-0000-000000000001"),
                positionProfileId = Guid.Parse("20000000-0000-0000-0000-000000000002")
            });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EmployeePayload>())!;
    }

    private async Task AssignManagerAsync(HttpClient client, Guid employeeId, Guid managerId)
    {
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/employees/{employeeId}/manager",
            new { companyId = SeededCompanyId, id = employeeId, managerId });
        response.EnsureSuccessStatusCode();
    }

    private Task CreateTaskAsync(
        string title,
        Guid assignedEmployeeId,
        TaskPriority priority = TaskPriority.Medium) =>
        TaskSeeder.SeedAsync(_factory, SeededCompanyId, title, priority: priority, assignedEmployeeId: assignedEmployeeId);

    private sealed record EmployeePayload(Guid Id);

    private sealed record ListPayload(IReadOnlyList<TeamTaskItem> Items);

    private sealed record TeamTaskItem(
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
