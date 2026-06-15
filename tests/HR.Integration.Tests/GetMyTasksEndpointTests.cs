using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

public class GetMyTasksEndpointTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly Guid SeededCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // ── Auth ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_MyTasks_Returns_Unauthorized_When_No_Auth_Header()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{SeededCompanyId}/tasks/mine");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Happy path ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_MyTasks_Returns_Empty_List_When_No_Tasks_Assigned()
    {
        var userId = Guid.NewGuid();
        using var client = AuthenticatedClient(userId);

        var response = await client.GetAsync($"/api/companies/{SeededCompanyId}/tasks/mine");

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

        // Create two tasks assigned to userA, one assigned to userB
        using var clientA = AuthenticatedClient(userA);
        using var clientB = AuthenticatedClient(userB);

        await CreateTaskAsync(clientA, "Task for A", assignedUserId: userA);
        await CreateTaskAsync(clientA, "Also for A", assignedUserId: userA);
        await CreateTaskAsync(clientA, "Task for B", assignedUserId: userB);

        var response = await clientA.GetAsync($"/api/companies/{SeededCompanyId}/tasks/mine");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.Equal(2, payload!.Items.Count);
        Assert.All(payload.Items, item => Assert.Equal(userA, item.AssignedUserId));
    }

    [Fact]
    public async Task Get_MyTasks_Filters_By_Status_When_Provided()
    {
        var userId = Guid.NewGuid();
        using var client = AuthenticatedClient(userId);

        await CreateTaskAsync(client, "Open task",      assignedUserId: userId, status: "Open");
        await CreateTaskAsync(client, "Another open",   assignedUserId: userId, status: "Open");

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/tasks/mine?status=Open");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.True(payload!.Items.Count >= 2);
        Assert.All(payload.Items, item => Assert.Equal("Open", item.Status));
    }

    [Fact]
    public async Task Get_MyTasks_Does_Not_Return_Unassigned_Tasks()
    {
        var userId = Guid.NewGuid();
        using var client = AuthenticatedClient(userId);

        // Create an unassigned task (no assignedUserId)
        await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/tasks",
            new { companyId = SeededCompanyId, title = "Unassigned", priority = "Low", source = "Manual" });

        var response = await client.GetAsync($"/api/companies/{SeededCompanyId}/tasks/mine");

        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.Empty(payload!.Items);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private HttpClient AuthenticatedClient(Guid userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        return client;
    }

    private async Task CreateTaskAsync(
        HttpClient client,
        string title,
        Guid? assignedUserId = null,
        string priority = "Medium",
        string source = "Manual",
        string status = "Open")
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/tasks",
            new
            {
                companyId = SeededCompanyId,
                title,
                priority,
                source,
                assignedUserId
            });
        response.EnsureSuccessStatusCode();
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
