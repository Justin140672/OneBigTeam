using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

public class CreateTaskEndpointTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly Guid SeededCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.NewGuid();

    // ── Auth ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_Task_Returns_Unauthorized_When_No_Auth_Header()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/tasks",
            new { title = "Test task", priority = "Medium", source = "Manual" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Happy path ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_Task_Returns_201_With_Location_Header()
    {
        using var client = AuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/tasks",
            new
            {
                companyId = SeededCompanyId,
                title = "Send offer letter",
                priority = "High",
                source = "Onboarding"
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Contains("/tasks/", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Post_Task_Returns_Correct_Payload()
    {
        using var client = AuthenticatedClient();
        var assignedEmployee = Guid.NewGuid();
        var due = "2026-12-31";

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/tasks",
            new
            {
                companyId = SeededCompanyId,
                title = "Complete right-to-work check",
                description = "Verify passport and upload to HR system",
                priority = "Critical",
                source = "Compliance",
                dueDate = due,
                assignedEmployeeId = assignedEmployee
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<TaskPayload>();
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload!.Id);
        Assert.Equal(SeededCompanyId, payload.CompanyId);
        Assert.Equal("Complete right-to-work check", payload.Title);
        Assert.Equal("Verify passport and upload to HR system", payload.Description);
        Assert.Equal("Open", payload.Status);
        Assert.Equal("Critical", payload.Priority);
        Assert.Equal("Compliance", payload.Source);
        Assert.Equal(due, payload.DueDate);
        Assert.Equal(assignedEmployee, payload.AssignedEmployeeId);
        Assert.Equal(UserId, payload.CreatedBy);
    }

    [Fact]
    public async Task Post_Task_Without_Optional_Fields_Returns_201()
    {
        using var client = AuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/tasks",
            new
            {
                companyId = SeededCompanyId,
                title = "Minimal task",
                priority = "Low",
                source = "Manual"
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<TaskPayload>();
        Assert.Null(payload!.Description);
        Assert.Null(payload.DueDate);
        Assert.Null(payload.AssignedEmployeeId);
        Assert.Null(payload.AssignedUserId);
    }

    [Fact]
    public async Task Post_Task_Sets_CreatedBy_From_Authenticated_User()
    {
        using var client = AuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{SeededCompanyId}/tasks",
            new
            {
                companyId = SeededCompanyId,
                title = "Verify identity documents",
                priority = "Medium",
                source = "Recruitment"
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<TaskPayload>();
        Assert.Equal(UserId, payload!.CreatedBy);
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
        Guid CreatedBy);
}
