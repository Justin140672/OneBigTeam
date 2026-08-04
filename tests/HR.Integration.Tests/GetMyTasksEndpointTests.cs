using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.SharedKernel;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetMyTasksEndpointTests(ApiWebApplicationFactory factory)
{
    private static readonly Guid SeededCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // ── Auth ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_MyTasks_Returns_Unauthorized_When_No_Auth_Header()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{SeededCompanyId}/tasks/my");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Happy path ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_MyTasks_Returns_Empty_List_When_No_Tasks_Assigned()
    {
        var userId = Guid.NewGuid();
        using var client = await AuthenticatedClient(userId);

        var response = await client.GetAsync($"/api/companies/{SeededCompanyId}/tasks/my");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_MyTasks_Returns_Only_Tasks_Assigned_To_Caller()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await TaskSeeder.SeedAsync(factory, SeededCompanyId, "Task for A", assignedUserId: userA);
        await TaskSeeder.SeedAsync(factory, SeededCompanyId, "Also for A", assignedUserId: userA);
        await TaskSeeder.SeedAsync(factory, SeededCompanyId, "Task for B", assignedUserId: userB);

        using var client = await AuthenticatedClient(userA);
        var response = await client.GetAsync($"/api/companies/{SeededCompanyId}/tasks/my");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.Equal(2, payload!.Items.Count);
        Assert.All(payload.Items, item => Assert.Equal(userA, item.AssignedUserId));
    }

    [Fact]
    public async Task Get_MyTasks_Filters_By_Status_When_Provided()
    {
        var userId = Guid.NewGuid();

        await TaskSeeder.SeedAsync(factory, SeededCompanyId, "Open task",    assignedUserId: userId);
        await TaskSeeder.SeedAsync(factory, SeededCompanyId, "Another open", assignedUserId: userId);

        using var client = await AuthenticatedClient(userId);
        var response = await client.GetAsync($"/api/companies/{SeededCompanyId}/tasks/my?status=Open");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.True(payload!.Items.Count >= 2);
        Assert.All(payload.Items, item => Assert.Equal("Open", item.Status));
    }

    [Fact]
    public async Task Get_MyTasks_Does_Not_Return_Unassigned_Tasks()
    {
        var userId = Guid.NewGuid();

        await TaskSeeder.SeedAsync(factory, SeededCompanyId, "Unassigned");

        using var client = await AuthenticatedClient(userId);
        var response = await client.GetAsync($"/api/companies/{SeededCompanyId}/tasks/my");

        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.Empty(payload!.Items);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<HttpClient> AuthenticatedClient(Guid userId)
    {
        TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.Employee).GetAwaiter().GetResult();

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.Employee, SeededCompanyId);
        return client;
    }

    private sealed record ListPayload(IReadOnlyList<TaskListItem> Items);

    private sealed record TaskListItem(
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
