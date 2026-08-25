using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

// OFF-01 regression coverage: cancelling a leaving process must not just mark the local
// OffboardingTask checklist rows Skipped — it must also cancel the corresponding Tasks-module
// TaskItems that were generated when offboarding auto-started. Before OFF-01, those TaskItems were
// left dangling Open even after the plan itself was shown as Cancelled.
[Collection("Integration")]
public class OffboardingTaskSynchronisationOnLeavingProcessCancelledTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("ffffffff-4100-0000-0000-000000000001");

    public OffboardingTaskSynchronisationOnLeavingProcessCancelledTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private static async Task<Guid> CreateEmployeeAsync(HttpClient client, Guid companyId)
    {
        var refData = await EmployeeReferenceDataSeeder.SeedViaApiAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            EmployeeReferenceDataSeeder.BuildCreateEmployeeRequest(
                companyId, refData, "Sync", "Employee", $"sync.{Guid.NewGuid():N}@example.com"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    // Relative to "today" — see other leaving-process tests for why a hardcoded near-term literal
    // eventually becomes "backdated".
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

    [Fact]
    public async Task CancelLeavingProcess_Cancels_Both_Offboarding_Plan_And_Tasks_Module_TaskItems()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User1, SystemRoles.HrAdministrator, companyId);

        var employeeId = await CreateEmployeeAsync(client, companyId);
        await StartLeavingProcessAsync(client, companyId, employeeId);

        // Starting the leaving process auto-starts offboarding (StartOffboardingHandler), which
        // creates a "Review outstanding documents for employee exit" OffboardingTask + a matching
        // unassigned Tasks-module TaskItem (sourceEntityId == the OffboardingTask's own id).
        var overviewBeforeResponse = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding-overview");
        overviewBeforeResponse.EnsureSuccessStatusCode();
        var overviewBefore = await overviewBeforeResponse.Content.ReadFromJsonAsync<OffboardingOverviewPayload>();
        Assert.NotNull(overviewBefore);
        Assert.True(overviewBefore!.HasPlan);
        Assert.Equal("InProgress", overviewBefore.PlanStatus);

        var documentReviewTask = Assert.Single(
            overviewBefore.Tasks, t => t.Title == "Review outstanding documents for employee exit");
        Assert.Equal("Pending", documentReviewTask.Status);

        var unassignedBeforeResponse = await client.GetAsync($"/api/companies/{companyId}/tasks/unassigned");
        unassignedBeforeResponse.EnsureSuccessStatusCode();
        var unassignedBefore = await unassignedBeforeResponse.Content.ReadFromJsonAsync<UnassignedTasksPayload>();
        Assert.NotNull(unassignedBefore);
        var matchingTaskItemBefore = Assert.Single(
            unassignedBefore!.Items, t => t.SourceEntityId == documentReviewTask.Id);
        Assert.Equal("Open", matchingTaskItemBefore.Status);
        Assert.Equal("Offboarding", matchingTaskItemBefore.Source);

        // Cancel the leaving process — this must cancel both the local OffboardingTask rows AND
        // the corresponding Tasks-module TaskItems.
        var cancelResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leaving-process/cancel",
            new { companyId, employeeId, cancellationReason = "Employee retracted resignation." });
        cancelResponse.EnsureSuccessStatusCode();

        var overviewAfterResponse = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding-overview");
        overviewAfterResponse.EnsureSuccessStatusCode();
        var overviewAfter = await overviewAfterResponse.Content.ReadFromJsonAsync<OffboardingOverviewPayload>();
        Assert.NotNull(overviewAfter);
        Assert.Equal("Cancelled", overviewAfter!.PlanStatus);

        var documentReviewTaskAfter = Assert.Single(
            overviewAfter.Tasks, t => t.Id == documentReviewTask.Id);
        Assert.Equal("Skipped", documentReviewTaskAfter.Status);

        // The core regression check: the Tasks-module TaskItem must no longer be Open — it must
        // have actually been cancelled, not just left dangling while the local plan/task rows
        // moved on. GetUnassignedTasks excludes Completed/Cancelled items, so its absence here
        // (rather than still showing "Open") is exactly the signal that OFF-01 exists to guarantee.
        var unassignedAfterResponse = await client.GetAsync($"/api/companies/{companyId}/tasks/unassigned");
        unassignedAfterResponse.EnsureSuccessStatusCode();
        var unassignedAfter = await unassignedAfterResponse.Content.ReadFromJsonAsync<UnassignedTasksPayload>();
        Assert.NotNull(unassignedAfter);
        Assert.DoesNotContain(unassignedAfter!.Items, t => t.SourceEntityId == documentReviewTask.Id);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record OffboardingOverviewPayload(
        Guid EmployeeId,
        bool HasPlan,
        string? PlanStatus,
        DateOnly? LastWorkingDay,
        string? Notes,
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
        DateTimeOffset UpdatedAt);

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
}
