using System.Net;
using System.Net.Http.Json;
using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Modules.Onboarding.Domain;
using HR.Modules.Onboarding.Persistence;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// DSH-06 stage 1: the HR bounded dashboard summary endpoint
/// (GET /api/companies/{companyId}/dashboards/hr/summary). Shares the cross-module
/// <see cref="HR.Infrastructure.Abstractions.IWorkloadActionProvider"/> fan-out with the Workload &amp;
/// HR Actions Report, so this locks (a) the endpoint's two-stage gate — the shared
/// "reporting:view-workload-actions" menu policy, then an in-endpoint "reporting:view-hr" narrowing —
/// and (b) that each provider's own row-level company scoping is honoured through the composer.
/// </summary>
[Collection("Integration")]
public class GetHrDashboardSummaryEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.Date);

    public GetHrDashboardSummaryEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static string Url(Guid companyId) => $"/api/companies/{companyId}/dashboards/hr/summary";

    private async Task<HttpClient> ClientFor(Guid companyId, Guid userId, params Guid[] roles)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        foreach (var role in roles)
        {
            await TestRoleSeeder.AssignRoleAsync(_factory, userId, role, companyId);
        }

        return client;
    }

    [Fact]
    public async Task Get_HrDashboardSummary_Returns_Unauthorized_For_Anonymous()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(Url(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_HrDashboardSummary_Returns_Forbidden_For_Plain_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.Employee);

        var response = await client.GetAsync(Url(companyId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_HrDashboardSummary_Returns_Forbidden_For_Manager_Without_Hr()
    {
        // Manager clears the shared "reporting:view-workload-actions" menu gate but fails the
        // in-endpoint "reporting:view-hr" narrowing.
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.Manager);

        var response = await client.GetAsync(Url(companyId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_HrDashboardSummary_Returns_Ok_With_Envelope_For_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);

        var response = await client.GetAsync(Url(companyId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SummaryPayload>();
        Assert.NotNull(payload);
        Assert.NotNull(payload!.Categories);
        // JSON contract lock — envelope flags always present even on a clean run.
        Assert.All(payload.Categories, c => Assert.False(string.IsNullOrWhiteSpace(c.Status)));
    }

    [Fact]
    public async Task Get_HrDashboardSummary_Does_Not_Leak_Another_Companys_Rows()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        var empA = await SeedEmployeeAsync(companyA, "Anna", "Alpha");
        var empB = await SeedEmployeeAsync(companyB, "Bruno", "Beta");

        await SeedLeaveRequestAsync(companyA, empA, Today.AddDays(5));
        await SeedLeaveRequestAsync(companyB, empB, Today.AddDays(5));
        await SeedOverdueTaskAsync(companyA, empA, Today.AddDays(-2));
        await SeedOverdueTaskAsync(companyB, empB, Today.AddDays(-2));

        using var client = await ClientFor(companyA, Guid.NewGuid(), SystemRoles.HrAdministrator);
        var payload = await client.GetFromJsonAsync<SummaryPayload>(Url(companyA));

        Assert.NotNull(payload);
        var allItems = payload!.Categories.SelectMany(c => c.Items).ToList();
        Assert.Contains(allItems, i => i.EmployeeId == empA);
        Assert.DoesNotContain(allItems, i => i.EmployeeId == empB);

        var leave = payload.Categories.SingleOrDefault(c => c.Category == "Pending Leave Approvals");
        Assert.NotNull(leave);
        Assert.Equal(1, leave!.ActionableCount);
    }

    [Fact]
    public async Task Get_HrDashboardSummary_Caps_Category_Items_At_25_But_Reports_Full_Count()
    {
        var companyId = Guid.NewGuid();
        await SeedManyPendingLeaveRequestsAsync(companyId, 30);

        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);
        var payload = await client.GetFromJsonAsync<SummaryPayload>(Url(companyId));

        Assert.NotNull(payload);
        var leave = payload!.Categories.Single(c => c.Category == "Pending Leave Approvals");
        Assert.Equal(30, leave.ActionableCount);
        Assert.Equal(25, leave.Items.Count);
        Assert.True(leave.IsTruncated);
        Assert.True(payload.TotalActionableCount >= 30);
    }

    [Fact]
    public async Task Get_HrDashboardSummary_Pending_Leave_Approval_Is_Not_OwnerActionable_For_Hr()
    {
        // A pending leave request's approval task is always owned by the employee's manager —
        // HR sees the row for company-wide oversight only, never as directly actionable.
        var companyId = Guid.NewGuid();
        var employeeId = await SeedEmployeeAsync(companyId, "Layla", "Leaver");
        await SeedLeaveRequestAsync(companyId, employeeId, Today.AddDays(5));

        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);
        var payload = await client.GetFromJsonAsync<SummaryPayload>(Url(companyId));

        Assert.NotNull(payload);
        var leave = payload!.Categories.Single(c => c.Category == "Pending Leave Approvals");
        var item = Assert.Single(leave.Items);
        Assert.False(item.IsOwnerActionable);
        Assert.NotNull(item.OwnerLabel);
    }

    [Fact]
    public async Task Get_HrDashboardSummary_Client_Cancellation_Is_Observed_As_Cancellation()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync(Url(companyId), cts.Token));
    }

    [Fact]
    public async Task Get_HrDashboardSummary_Outstanding_Onboarding_Task_Resolves_Linked_TaskId_When_Open_Task_Exists()
    {
        var companyId = Guid.NewGuid();
        var employeeId = await SeedEmployeeAsync(companyId, "Nadia", "Newstarter");
        var onboardingTaskId = await SeedOutstandingOnboardingTaskAsync(companyId, employeeId);
        var linkedTaskId = await SeedOpenTaskLinkedToSourceAsync(
            companyId, onboardingTaskId, TaskSource.Onboarding, TaskActionType.Complete, employeeId);

        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);
        var payload = await client.GetFromJsonAsync<SummaryPayload>(Url(companyId));

        Assert.NotNull(payload);
        var category = payload!.Categories.Single(c => c.Category == "Outstanding Onboarding Tasks");
        var item = Assert.Single(category.Items);
        Assert.Equal(linkedTaskId, item.TaskId);
        Assert.Equal("", item.DeepLinkUrl);
    }

    [Fact]
    public async Task Get_HrDashboardSummary_Outstanding_Onboarding_Task_Has_Null_TaskId_When_No_Open_Task_Exists()
    {
        var companyId = Guid.NewGuid();
        var employeeId = await SeedEmployeeAsync(companyId, "Nadia", "Newstarter");
        await SeedOutstandingOnboardingTaskAsync(companyId, employeeId);
        // No Tasks-module TaskItem seeded — the onboarding task has no linked open task.

        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);
        var payload = await client.GetFromJsonAsync<SummaryPayload>(Url(companyId));

        Assert.NotNull(payload);
        var category = payload!.Categories.Single(c => c.Category == "Outstanding Onboarding Tasks");
        var item = Assert.Single(category.Items);
        Assert.Null(item.TaskId);
        Assert.Equal("", item.DeepLinkUrl);
    }

    [Fact]
    public async Task Get_HrDashboardSummary_Multiple_Outstanding_Onboarding_Tasks_With_Same_Title_Resolve_Distinct_TaskIds()
    {
        // Two different employees each have an onboarding task with the identical title — proves
        // resolution is keyed by the onboarding task's own id, not by title/employee matching.
        var companyId = Guid.NewGuid();
        var employeeA = await SeedEmployeeAsync(companyId, "Anna", "Alpha");
        var employeeB = await SeedEmployeeAsync(companyId, "Bruno", "Beta");

        var onboardingTaskIdA = await SeedOutstandingOnboardingTaskAsync(companyId, employeeA, "Set up laptop");
        var onboardingTaskIdB = await SeedOutstandingOnboardingTaskAsync(companyId, employeeB, "Set up laptop");

        var linkedTaskIdA = await SeedOpenTaskLinkedToSourceAsync(
            companyId, onboardingTaskIdA, TaskSource.Onboarding, TaskActionType.Complete, employeeA);
        var linkedTaskIdB = await SeedOpenTaskLinkedToSourceAsync(
            companyId, onboardingTaskIdB, TaskSource.Onboarding, TaskActionType.Complete, employeeB);

        using var client = await ClientFor(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);
        var payload = await client.GetFromJsonAsync<SummaryPayload>(Url(companyId));

        Assert.NotNull(payload);
        var category = payload!.Categories.Single(c => c.Category == "Outstanding Onboarding Tasks");
        Assert.Equal(2, category.Items.Count);
        Assert.Contains(category.Items, i => i.EmployeeId == employeeA && i.TaskId == linkedTaskIdA);
        Assert.Contains(category.Items, i => i.EmployeeId == employeeB && i.TaskId == linkedTaskIdB);
        Assert.NotEqual(
            category.Items.Single(i => i.EmployeeId == employeeA).TaskId,
            category.Items.Single(i => i.EmployeeId == employeeB).TaskId);
    }

    // ── Seeding helpers (mirrors GetWorkloadActionsEndpointTests) ─────────────

    private async Task<Guid> SeedEmployeeAsync(Guid companyId, string firstName, string lastName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        var refData = await EmployeeReferenceDataSeeder.SeedAsync(db, companyId);
        var employee = Employee.Create(
            Guid.NewGuid(), companyId, firstName, lastName,
            $"{firstName}.{lastName}.{Guid.NewGuid():N}@example.com".ToLowerInvariant(),
            new DateOnly(2026, 1, 1), hasSystemAccess: true, new DateOnly(1990, 1, 1),
            "British", "Prefer not to say", $"EMP-{Guid.NewGuid():N}",
            refData.EmploymentTypeId, refData.DepartmentId, refData.LocationId, refData.PositionProfileId, Now);
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee.Id;
    }

    private async Task SeedLeaveRequestAsync(Guid companyId, Guid employeeId, DateOnly startDate)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        db.LeaveRequests.Add(LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(), Guid.NewGuid(),
            startDate, LeaveDayPart.FullDay, startDate.AddDays(3), LeaveDayPart.FullDay,
            3m, "Trip", Now));
        await db.SaveChangesAsync();
    }

    private async Task SeedManyPendingLeaveRequestsAsync(Guid companyId, int count)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var requests = Enumerable.Range(0, count).Select(i => LeaveRequest.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Today.AddDays(5), LeaveDayPart.FullDay, Today.AddDays(8), LeaveDayPart.FullDay,
            3m, "Trip", Now.AddSeconds(i)));
        db.LeaveRequests.AddRange(requests);
        await db.SaveChangesAsync();
    }

    private async Task SeedOverdueTaskAsync(Guid companyId, Guid assignedEmployeeId, DateOnly dueDate)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        db.TaskItems.Add(TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), "Complete document check", null,
            TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete, dueDate,
            assignedEmployeeId, null, Now));
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedOutstandingOnboardingTaskAsync(Guid companyId, Guid employeeId, string title = "Set up laptop")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OnboardingDbContext>();
        var plan = OnboardingPlan.Create(Guid.NewGuid(), companyId, employeeId, Today, null, Now);
        db.OnboardingPlans.Add(plan);
        var task = OnboardingTask.Create(
            Guid.NewGuid(), companyId, plan.Id, title, null,
            OnboardingTemplateTaskAssignTo.Manager, Today.AddDays(5), Now);
        db.OnboardingTasks.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }

    /// <summary>Seeds a real open Tasks-module TaskItem whose SourceEntityId links back to a source-module task/record id.</summary>
    private async Task<Guid> SeedOpenTaskLinkedToSourceAsync(
        Guid companyId, Guid sourceEntityId, TaskSource source, TaskActionType actionType, Guid assignedEmployeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        var task = TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), "Linked task", null,
            TaskPriority.Medium, source, actionType, Today.AddDays(5),
            assignedEmployeeId, null, Now, sourceEntityId: sourceEntityId);
        db.TaskItems.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }

    private sealed record SummaryPayload(
        List<CategoryPayload> Categories,
        int TotalActionableCount,
        bool AllRequiredLoaded,
        bool HasPartialFailure,
        DateOnly AsOfDate);

    private sealed record CategoryPayload(
        string Category,
        string Status,
        bool Required,
        int ActionableCount,
        bool IsTruncated,
        List<ActionItemPayload> Items);

    private sealed record ActionItemPayload(
        Guid? EmployeeId,
        string EmployeeName,
        string? Department,
        string ActionType,
        string Category,
        DateOnly? DueDate,
        string Urgency,
        bool IsOverdue,
        string Status,
        string DeepLinkUrl,
        Guid? TaskId,
        bool IsOwnerActionable = true,
        string? OwnerLabel = null);
}
