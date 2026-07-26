using System.Net;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Tasks.Domain;
using HR.SharedKernel;

namespace HR.Integration.Tests;

public class CompleteTaskEndpointTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly Guid SeededCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // ── Auth ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Complete_Task_Returns_Unauthorized_When_No_Auth_Header()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/companies/{SeededCompanyId}/tasks/{Guid.NewGuid()}/complete",
            EmptyJson());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Not found ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Complete_Task_Returns_NotFound_When_Task_Does_Not_Exist()
    {
        using var client = AuthenticatedClient(Guid.NewGuid());

        var response = await client.PostAsync(
            $"/api/companies/{SeededCompanyId}/tasks/{Guid.NewGuid()}/complete",
            EmptyJson());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Conflict ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Complete_Task_Returns_Conflict_When_Task_Is_Cancelled()
    {
        var userId = Guid.NewGuid();
        using var client = AuthenticatedClient(userId);

        var cancelledTaskId = await SeedCancelledTaskAsync();

        var response = await client.PostAsync(
            $"/api/companies/{SeededCompanyId}/tasks/{cancelledTaskId}/complete",
            EmptyJson());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ── Happy path ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Complete_Task_Returns_Completed_Status()
    {
        var userId = Guid.NewGuid();
        using var client = AuthenticatedClient(userId);

        var taskId = await CreateTaskAsync("Task to complete");

        var response = await client.PostAsync(
            $"/api/companies/{SeededCompanyId}/tasks/{taskId}/complete",
            EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<TaskPayload>();
        Assert.Equal("Completed", payload!.Status);
        Assert.Equal(userId, payload.CompletedBy);
        Assert.NotNull(payload.CompletedAt);
    }

    [Fact]
    public async Task Complete_Task_Is_Idempotent()
    {
        var userId = Guid.NewGuid();
        using var client = AuthenticatedClient(userId);

        var taskId = await CreateTaskAsync("Task to complete twice");

        await client.PostAsync($"/api/companies/{SeededCompanyId}/tasks/{taskId}/complete", EmptyJson());
        var response = await client.PostAsync($"/api/companies/{SeededCompanyId}/tasks/{taskId}/complete", EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<TaskPayload>();
        Assert.Equal("Completed", payload!.Status);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private HttpClient AuthenticatedClient(Guid userId)
    {
        TestRoleSeeder.AssignRoleAsync(factory, userId, SystemRoles.Employee).GetAwaiter().GetResult();

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        return client;
    }

    private Task<Guid> CreateTaskAsync(string title) =>
        TaskSeeder.SeedAsync(factory, SeededCompanyId, title);

    private Task<Guid> SeedCancelledTaskAsync() =>
        TaskSeeder.SeedAsync(factory, SeededCompanyId, "Cancelled task", status: TaskItemStatus.Cancelled);

    private static StringContent EmptyJson() =>
        new("{}", Encoding.UTF8, "application/json");

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
