using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;

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
    public async Task Get_Task_Returns_NotFound_When_Task_Belongs_To_Different_Company()
    {
        using var client = AuthenticatedClient();

        // Create a task under the seeded company
        var createResponse = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/tasks",
            new { companyId = SeededCompanyId, title = "Private task", priority = "Low", source = "Manual" });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<TaskPayload>();

        // Attempt to fetch it under a different company ID
        var response = await client.GetAsync(
            $"/api/companies/{Guid.NewGuid()}/tasks/{created!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Happy path ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_Task_Returns_200_With_Full_Payload()
    {
        using var client = AuthenticatedClient();
        var assignedEmployee = Guid.NewGuid();

        var createResponse = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/tasks",
            new
            {
                companyId = SeededCompanyId,
                title = "Schedule probation review",
                description = "Book 1-to-1 with line manager",
                priority = "High",
                source = "Probation",
                dueDate = "2026-09-01",
                assignedEmployeeId = assignedEmployee
            });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<TaskPayload>();

        var response = await client.GetAsync(
            $"/api/companies/{SeededCompanyId}/tasks/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<TaskPayload>();
        Assert.NotNull(payload);
        Assert.Equal(created.Id, payload!.Id);
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

    [Fact]
    public async Task Get_Task_Returns_Location_Header_From_Create_Which_Resolves_To_200()
    {
        using var client = AuthenticatedClient();

        var createResponse = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/tasks",
            new { companyId = SeededCompanyId, title = "Follow-up task", priority = "Medium", source = "Workflow" });
        createResponse.EnsureSuccessStatusCode();

        var location = createResponse.Headers.Location!.ToString();
        var getResponse = await client.GetAsync(location);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
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
