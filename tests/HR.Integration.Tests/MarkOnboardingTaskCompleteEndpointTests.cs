using System.Net;
using System.Net.Http.Json;
using System.Text;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

/// <summary>
/// Covers POST /api/company-onboarding/checklist/tasks/{taskKey}/mark-complete
/// (MarkOnboardingTaskComplete): the onboarding:manage policy, an unknown task key => 404, the
/// happy path for a real registry task key, and that re-marking an already-complete task is
/// idempotent (SetStatus(true) again, still 200).
/// </summary>
[Collection("Integration")]
public class MarkOnboardingTaskCompleteEndpointTests
{
    private const string KnownTaskKey = "download-employee-import-template";

    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid HrAdminUserId  = Guid.NewGuid();
    private static readonly Guid EmployeeUserId = Guid.NewGuid();

    public MarkOnboardingTaskCompleteEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, EmployeeUserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientFor(Guid companyId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }

    private static StringContent EmptyJson() => new("{}", Encoding.UTF8, "application/json");

    [Fact]
    public async Task Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/company-onboarding/checklist/tasks/{KnownTaskKey}/mark-complete", EmptyJson());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_For_Employee_Role()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, EmployeeUserId);

        var response = await client.PostAsync(
            $"/api/company-onboarding/checklist/tasks/{KnownTaskKey}/mark-complete", EmptyJson());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_NotFound_For_An_Unknown_Task_Key()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, HrAdminUserId);

        var response = await client.PostAsync(
            "/api/company-onboarding/checklist/tasks/not-a-real-task-key/mark-complete", EmptyJson());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Marks_A_Known_Task_Complete_For_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, HrAdminUserId);

        var response = await client.PostAsync(
            $"/api/company-onboarding/checklist/tasks/{KnownTaskKey}/mark-complete", EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<MarkCompletePayload>();
        Assert.Equal(KnownTaskKey, payload!.TaskKey);
        Assert.True(payload.IsCompleted);
        Assert.NotNull(payload.CompletedAt);
    }

    [Fact]
    public async Task Re_Marking_An_Already_Complete_Task_Is_Idempotent()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, HrAdminUserId);

        var first  = await client.PostAsync(
            $"/api/company-onboarding/checklist/tasks/{KnownTaskKey}/mark-complete", EmptyJson());
        var second = await client.PostAsync(
            $"/api/company-onboarding/checklist/tasks/{KnownTaskKey}/mark-complete", EmptyJson());

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var payload = await second.Content.ReadFromJsonAsync<MarkCompletePayload>();
        Assert.True(payload!.IsCompleted);
    }

    [Fact]
    public async Task Completion_Is_Reflected_In_The_Onboarding_Checklist()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, HrAdminUserId);

        await client.PostAsync(
            $"/api/company-onboarding/checklist/tasks/{KnownTaskKey}/mark-complete", EmptyJson());

        var checklist = await client.GetFromJsonAsync<ChecklistPayload>("/api/company-onboarding/checklist");
        Assert.Contains(checklist!.Tasks, t => t.Key == KnownTaskKey && t.IsCompleted);
    }

    private sealed record MarkCompletePayload(string TaskKey, bool IsCompleted, DateTimeOffset? CompletedAt);
    private sealed record ChecklistTaskPayload(string Key, bool IsCompleted);
    private sealed record ChecklistPayload(List<ChecklistTaskPayload> Tasks);
}
