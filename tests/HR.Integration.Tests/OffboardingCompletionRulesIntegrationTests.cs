using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// OFF-07: exercises "Define and enforce offboarding completion rules" end-to-end through the real
// HTTP API — mandatory-vs-optional completion gating, the "Skip" outcome requiring a reason, the
// final HR completion-review task, and the Postgres row-lock concurrency guarantee around plan
// completion (this suite runs against a real Postgres instance, unlike the InMemory-provider unit
// tests in HR.Modules.Offboarding.Tests, which cannot exercise the `FOR UPDATE` raw SQL statement).
[Collection("Integration")]
public class OffboardingCompletionRulesIntegrationTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("ff070001-0000-0000-0000-000000000001");

    public OffboardingCompletionRulesIntegrationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User1, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private static async Task<Guid> CreateEmployeeAsync(HttpClient client, Guid companyId)
    {
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Off07", "Employee", $"off07.{Guid.NewGuid():N}@example.com"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    // Relative to "today" so this test never becomes "backdated" as time passes.
    private static readonly DateOnly LeavingDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
    private static readonly DateOnly LastWorkingDay = LeavingDate.AddDays(-1);

    private static async Task StartLeavingProcessAsync(HttpClient client, Guid companyId, Guid employeeId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process",
            new
            {
                companyId,
                employeeId,
                resignationReceivedDate = LeavingDate.AddDays(-30).ToString("yyyy-MM-dd"),
                leavingDate = LeavingDate.ToString("yyyy-MM-dd"),
                lastWorkingDay = LastWorkingDay.ToString("yyyy-MM-dd"),
                leavingReason = "Resignation"
            });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<OffboardingOverviewPayload> GetOverviewAsync(
        HttpClient client, Guid companyId, Guid employeeId)
    {
        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/offboarding-overview");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OffboardingOverviewPayload>())!;
    }

    private static async Task<Guid> FindTaskItemIdBySourceEntityAsync(
        HttpClient client, Guid companyId, Guid employeeId, Guid sourceEntityId)
    {
        var unassignedResponse = await client.GetAsync($"/api/companies/{companyId}/tasks/unassigned");
        unassignedResponse.EnsureSuccessStatusCode();
        var unassignedPayload = await unassignedResponse.Content.ReadFromJsonAsync<UnassignedTasksPayload>();
        var unassignedMatch = unassignedPayload!.Items.SingleOrDefault(t => t.SourceEntityId == sourceEntityId);
        if (unassignedMatch is not null)
            return unassignedMatch.Id;

        var employeeResponse = await client.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/tasks");
        employeeResponse.EnsureSuccessStatusCode();
        var employeePayload = await employeeResponse.Content.ReadFromJsonAsync<EmployeeTasksPayload>();
        var employeeMatch = Assert.Single(employeePayload!.Items, t => t.SourceEntityId == sourceEntityId);
        return employeeMatch.Id;
    }

    private static async Task<HttpResponseMessage> CompleteTaskAsync(
        HttpClient client, Guid companyId, Guid taskItemId, string? outcomeDecision = null, string? outcomeReason = null) =>
        await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/tasks/{taskItemId}/complete",
            new { companyId, id = taskItemId, outcomeDecision, outcomeReason });

    [Fact]
    public async Task CompletingEveryMandatoryTask_Completes_Plan_And_Creates_HR_Completion_Review_Task()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var employeeId = await CreateEmployeeAsync(client, companyId);

        await StartLeavingProcessAsync(client, companyId, employeeId);

        var overview = await GetOverviewAsync(client, companyId, employeeId);
        Assert.Equal("InProgress", overview.PlanStatus);
        Assert.NotEmpty(overview.Tasks);

        foreach (var task in overview.Tasks)
        {
            var taskItemId = await FindTaskItemIdBySourceEntityAsync(client, companyId, employeeId, task.Id);
            var resp = await CompleteTaskAsync(client, companyId, taskItemId);
            resp.EnsureSuccessStatusCode();
        }

        var overviewAfter = await GetOverviewAsync(client, companyId, employeeId);
        Assert.Equal("Completed", overviewAfter.PlanStatus);
        Assert.Equal(overviewAfter.TotalTasks, overviewAfter.ResolvedTasks);
        Assert.Equal(100, overviewAfter.ProgressPercent);

        // The final HR completion-review task is assigned to this test's own HR administrator (the
        // only one in this company), so it surfaces via "my tasks".
        var myTasksResponse = await client.GetAsync($"/api/companies/{companyId}/tasks/my");
        myTasksResponse.EnsureSuccessStatusCode();
        var myTasks = await myTasksResponse.Content.ReadFromJsonAsync<MyTasksPayload>();
        var reviewTask = Assert.Single(
            myTasks!.Items, t => t.Source == "Offboarding" && t.ActionType == "Review");
        Assert.Equal("High", reviewTask.Priority);
    }

    [Fact]
    public async Task Plan_Does_Not_Complete_While_A_Mandatory_Task_Is_Outstanding_Even_If_Every_Other_Task_Is_Resolved()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var employeeId = await CreateEmployeeAsync(client, companyId);

        await StartLeavingProcessAsync(client, companyId, employeeId);

        var overview = await GetOverviewAsync(client, companyId, employeeId);
        Assert.True(overview.Tasks.Count > 0);

        // Leave exactly one task (the checklist's manager exit-interview task, if present, otherwise
        // any single mandatory task) outstanding — complete every other one.
        var taskToLeaveOutstanding = overview.Tasks[0];
        foreach (var task in overview.Tasks.Where(t => t.Id != taskToLeaveOutstanding.Id))
        {
            var taskItemId = await FindTaskItemIdBySourceEntityAsync(client, companyId, employeeId, task.Id);
            var resp = await CompleteTaskAsync(client, companyId, taskItemId);
            resp.EnsureSuccessStatusCode();
        }

        var overviewStillOpen = await GetOverviewAsync(client, companyId, employeeId);
        Assert.Equal("InProgress", overviewStillOpen.PlanStatus);
        Assert.NotEqual(overviewStillOpen.TotalTasks, overviewStillOpen.ResolvedTasks);
    }

    [Fact]
    public async Task CompleteTask_With_Skip_Outcome_And_No_Reason_Leaves_Task_Outstanding()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var employeeId = await CreateEmployeeAsync(client, companyId);

        await StartLeavingProcessAsync(client, companyId, employeeId);

        var overview = await GetOverviewAsync(client, companyId, employeeId);
        var task = overview.Tasks[0];
        var taskItemId = await FindTaskItemIdBySourceEntityAsync(client, companyId, employeeId, task.Id);

        var resp = await CompleteTaskAsync(client, companyId, taskItemId, outcomeDecision: "Skip");
        resp.EnsureSuccessStatusCode(); // Best-effort action — the HTTP call itself still succeeds.

        var overviewAfter = await GetOverviewAsync(client, companyId, employeeId);
        var taskAfter = Assert.Single(overviewAfter.Tasks, t => t.Id == task.Id);
        Assert.Equal("Pending", taskAfter.Status);
        Assert.Null(taskAfter.SkipReason);
    }

    [Fact]
    public async Task CompleteTask_With_Skip_Outcome_And_Reason_Marks_Task_Skipped_With_Reason_Recorded()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var employeeId = await CreateEmployeeAsync(client, companyId);

        await StartLeavingProcessAsync(client, companyId, employeeId);

        var overview = await GetOverviewAsync(client, companyId, employeeId);
        var task = overview.Tasks[0];
        var taskItemId = await FindTaskItemIdBySourceEntityAsync(client, companyId, employeeId, task.Id);

        var resp = await CompleteTaskAsync(
            client, companyId, taskItemId, outcomeDecision: "Skip", outcomeReason: "No longer applicable.");
        resp.EnsureSuccessStatusCode();

        var overviewAfter = await GetOverviewAsync(client, companyId, employeeId);
        var taskAfter = Assert.Single(overviewAfter.Tasks, t => t.Id == task.Id);
        Assert.Equal("Skipped", taskAfter.Status);
        Assert.Equal("No longer applicable.", taskAfter.SkipReason);
    }

    [Fact]
    public async Task GetOffboardingOverview_Requires_Authentication()
    {
        var companyId = Guid.NewGuid();
        using var anonymousClient = _factory.CreateClient();
        anonymousClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await anonymousClient.GetAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/offboarding-overview");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CompleteTask_Requires_Authentication()
    {
        var companyId = Guid.NewGuid();
        using var anonymousClient = _factory.CreateClient();
        anonymousClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var response = await anonymousClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/tasks/{Guid.NewGuid()}/complete",
            new { companyId, id = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // OFF-07: the real point of TryCompletePlanAsync's row lock (`SELECT ... FOR UPDATE`) — firing
    // two CompleteTask requests for a plan's last two outstanding mandatory tasks at "the same time"
    // must still only ever complete the plan (and create its HR review task) exactly once, never
    // twice. This only exercises the real guarantee against Postgres (unlike the InMemory-provider
    // unit tests), since InMemory doesn't support the transaction/raw-SQL FOR UPDATE statement used.
    [Fact]
    public async Task Concurrently_Completing_The_Last_Two_Mandatory_Tasks_Completes_The_Plan_Exactly_Once()
    {
        var companyId = Guid.NewGuid();
        using var client = await AdminClient(companyId);
        var employeeId = await CreateEmployeeAsync(client, companyId);

        await StartLeavingProcessAsync(client, companyId, employeeId);

        var overview = await GetOverviewAsync(client, companyId, employeeId);
        Assert.True(overview.Tasks.Count >= 2, "This scenario needs at least two checklist tasks.");

        var lastTwo = overview.Tasks.Take(2).ToList();
        var rest = overview.Tasks.Skip(2).ToList();

        // Resolve every task except the last two.
        foreach (var task in rest)
        {
            var taskItemId = await FindTaskItemIdBySourceEntityAsync(client, companyId, employeeId, task.Id);
            var resp = await CompleteTaskAsync(client, companyId, taskItemId);
            resp.EnsureSuccessStatusCode();
        }

        var taskItemIds = new List<Guid>();
        foreach (var task in lastTwo)
            taskItemIds.Add(await FindTaskItemIdBySourceEntityAsync(client, companyId, employeeId, task.Id));

        // Fire both completions "concurrently" using independent HttpClients against the same
        // running host, so they hit separate DbContexts/transactions like two real simultaneous
        // requests would.
        using var client2 = await AdminClient(companyId);

        var task1 = CompleteTaskAsync(client, companyId, taskItemIds[0]);
        var task2 = CompleteTaskAsync(client2, companyId, taskItemIds[1]);
        var responses = await Task.WhenAll(task1, task2);

        foreach (var response in responses)
            response.EnsureSuccessStatusCode();

        var overviewAfter = await GetOverviewAsync(client, companyId, employeeId);
        Assert.Equal("Completed", overviewAfter.PlanStatus);

        var myTasksResponse = await client.GetAsync($"/api/companies/{companyId}/tasks/my");
        myTasksResponse.EnsureSuccessStatusCode();
        var myTasks = await myTasksResponse.Content.ReadFromJsonAsync<MyTasksPayload>();

        // Exactly one HR completion-review task must have been created for this plan — not two.
        var reviewTasks = myTasks!.Items
            .Where(t => t.Source == "Offboarding" && t.ActionType == "Review")
            .ToList();
        Assert.Single(reviewTasks);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record OffboardingOverviewPayload(
        Guid EmployeeId,
        bool HasPlan,
        string? PlanStatus,
        DateOnly? LastWorkingDay,
        string? Notes,
        bool IsBackdated,
        bool RequiresHrReconciliation,
        bool HasIncompleteOffboardingAtDeparture,
        int TotalTasks,
        int ResolvedTasks,
        int ProgressPercent,
        IReadOnlyList<OffboardingTaskOverviewItemPayload> Tasks);

    private sealed record OffboardingTaskOverviewItemPayload(
        Guid Id,
        string Title,
        string? Description,
        string AssignTo,
        string Status,
        DateOnly? DueDate,
        DateTimeOffset? CompletedAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        bool RequiresHrConfirmation,
        bool IsMandatory,
        string? SkipReason,
        Guid? SkippedByUserId,
        DateTimeOffset? SkippedAt);

    private sealed record UnassignedTasksPayload(IReadOnlyList<UnassignedTaskItemPayload> Items);

    private sealed record UnassignedTaskItemPayload(
        Guid Id,
        Guid CompanyId,
        string Title,
        string? Description,
        string Status,
        string Priority,
        string Source,
        string ActionType,
        DateOnly? DueDate,
        Guid? SourceEntityId,
        Guid CreatedBy,
        DateTimeOffset CreatedAt);

    private sealed record EmployeeTasksPayload(IReadOnlyList<EmployeeTaskItemPayload> Items);

    private sealed record EmployeeTaskItemPayload(
        Guid Id,
        Guid CompanyId,
        string Title,
        string? Description,
        string Status,
        string Priority,
        string Source,
        string ActionType,
        DateOnly? DueDate,
        Guid? AssignedEmployeeId,
        Guid? AssignedUserId,
        string? AssignedEmployeeName,
        Guid CreatedBy,
        Guid? CompletedBy,
        DateTimeOffset? CompletedAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        Guid? SourceEntityId);

    private sealed record MyTasksPayload(IReadOnlyList<MyTaskItemPayload> Items);

    private sealed record MyTaskItemPayload(
        Guid Id,
        Guid CompanyId,
        string Title,
        string? Description,
        string Status,
        string Priority,
        string Source,
        string ActionType,
        DateOnly? DueDate,
        Guid? AssignedEmployeeId,
        Guid? AssignedUserId,
        string? AssignedEmployeeName,
        Guid CreatedBy,
        Guid? CompletedBy,
        DateTimeOffset? CompletedAt,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
