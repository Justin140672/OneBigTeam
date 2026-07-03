using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Integration.Tests;

public class GetTaskEndpointTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
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
        using var client = AuthenticatedClient();

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/tasks/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Task_Returns_Forbidden_When_Route_Company_Does_Not_Match_Auth_Tenant()
    {
        var taskId = await TaskSeeder.SeedAsync(factory, SeededCompanyId, "Private task", priority: TaskPriority.Low);

        // Authenticated as SeededCompanyId but route targets a different company — middleware blocks it.
        using var client = AuthenticatedClient();
        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/tasks/{taskId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Happy path ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_Task_Returns_200_With_Full_Payload()
    {
        var assignedEmployee = Guid.NewGuid();
        var taskId = await TaskSeeder.SeedAsync(
            factory, SeededCompanyId,
            title: "Schedule probation review",
            description: "Book 1-to-1 with line manager",
            priority: TaskPriority.High,
            source: TaskSource.Probation,
            dueDate: new DateOnly(2026, 9, 1),
            assignedEmployeeId: assignedEmployee,
            createdBy: UserId);

        using var client = AuthenticatedClient();
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

    // ── Helpers ────────────────────────────────────────────────────────────────

    private HttpClient AuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, SeededCompanyId.ToString());
        return client;
    }

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
