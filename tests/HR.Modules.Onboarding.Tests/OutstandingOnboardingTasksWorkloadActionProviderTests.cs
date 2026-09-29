using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Onboarding.Services;
using HR.Modules.Onboarding.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Onboarding.Tests;

public class OutstandingOnboardingTasksWorkloadActionProviderTests
{
    private static ClaimsPrincipal CallerWithSub(Guid employeeId) =>
        new(new ClaimsIdentity([new Claim("sub", employeeId.ToString())]));

    private static OnboardingReportItem BuildItem(Guid employeeId, params OnboardingReportTaskItem[] tasks) =>
        new(employeeId, Guid.NewGuid(), "InProgress", new DateOnly(2026, 7, 1), tasks.Length, 0, tasks);

    [Fact]
    public async Task HrCaller_Sees_All_Outstanding_Tasks_CompanyWide()
    {
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var reader = new FakeOnboardingReportReader(
        [
            BuildItem(employeeA, new OnboardingReportTaskItem("Set up laptop", null, "HR", false)),
            BuildItem(employeeB, new OnboardingReportTaskItem("Complete induction", null, "Manager", false)),
        ]);

        var provider = new OutstandingOnboardingTasksWorkloadActionProvider(
            reader, new FakeDirectReportsReader(), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-hr"), new FakeOpenTaskBySourceEntityReader(), new FakeCurrentUser(callerId));

        var result = await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(callerId), WorkloadScope.Hr, CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task ManagerCaller_Is_Scoped_To_DirectReports_Only()
    {
        var directReportId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var reader = new FakeOnboardingReportReader(
        [
            BuildItem(directReportId, new OnboardingReportTaskItem("Task A", null, "Manager", false)),
            BuildItem(otherEmployeeId, new OnboardingReportTaskItem("Task B", null, "Manager", false)),
        ]);

        var provider = new OutstandingOnboardingTasksWorkloadActionProvider(
            reader, new FakeDirectReportsReader([directReportId]), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-onboarding"), new FakeOpenTaskBySourceEntityReader(), new FakeCurrentUser(callerId));

        var result = await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(callerId), WorkloadScope.Manager, CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal(directReportId, action.EmployeeId);
    }

    [Fact]
    public async Task ManagerWithNoDirectReports_Returns_Empty()
    {
        var callerId = Guid.NewGuid();
        var reader = new FakeOnboardingReportReader(
        [
            BuildItem(Guid.NewGuid(), new OnboardingReportTaskItem("Task A", null, "Manager", false)),
        ]);

        var provider = new OutstandingOnboardingTasksWorkloadActionProvider(
            reader, new FakeDirectReportsReader([]), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-onboarding"), new FakeOpenTaskBySourceEntityReader(), new FakeCurrentUser(callerId));

        var result = await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(callerId), WorkloadScope.Manager, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ManagerCaller_With_No_Resolved_CurrentUserId_Returns_Empty()
    {
        // currentUser.UserId is null even though the caller passes the manager policy — must not
        // throw or fall through to the HR-wide branch, must simply return empty.
        var reader = new FakeOnboardingReportReader(
        [
            BuildItem(Guid.NewGuid(), new OnboardingReportTaskItem("Task A", null, "Manager", false)),
        ]);

        var provider = new OutstandingOnboardingTasksWorkloadActionProvider(
            reader, new FakeDirectReportsReader([Guid.NewGuid()]), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-onboarding"), new FakeOpenTaskBySourceEntityReader(), new FakeCurrentUser(null));

        var result = await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(Guid.NewGuid()), WorkloadScope.Manager, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task CallerWithNoRecognisedRole_Returns_Empty_Not_Throws()
    {
        var callerId = Guid.NewGuid();
        var reader = new FakeOnboardingReportReader(
        [
            BuildItem(Guid.NewGuid(), new OnboardingReportTaskItem("Task A", null, "Manager", false)),
        ]);

        var provider = new OutstandingOnboardingTasksWorkloadActionProvider(
            reader, new FakeDirectReportsReader(), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService(), new FakeOpenTaskBySourceEntityReader(), new FakeCurrentUser(callerId));

        var result = await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(callerId), WorkloadScope.Manager, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Maps_ActionType_Status_DueDate_And_ResolvedTaskId()
    {
        var employeeId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var dueDate = new DateOnly(2026, 8, 5);
        var onboardingTaskId = Guid.NewGuid();
        var linkedTaskId = Guid.NewGuid();
        var reader = new FakeOnboardingReportReader(
        [
            BuildItem(employeeId, new OnboardingReportTaskItem("Set up laptop", dueDate, "HR", false, onboardingTaskId)),
        ]);

        var provider = new OutstandingOnboardingTasksWorkloadActionProvider(
            reader, new FakeDirectReportsReader(), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-hr"),
            new FakeOpenTaskBySourceEntityReader(new Dictionary<Guid, Guid> { [onboardingTaskId] = linkedTaskId }),
            new FakeCurrentUser(callerId));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(callerId), WorkloadScope.Hr, CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal("Set up laptop", action.ActionType);
        Assert.Equal("Outstanding Onboarding Tasks", action.ActionCategory);
        Assert.Equal("Outstanding", action.Status);
        Assert.Equal(dueDate, action.DueDate);
        Assert.Equal(linkedTaskId, action.TaskId);
        Assert.Equal("", action.DeepLinkUrl);
    }

    [Fact]
    public async Task Task_Without_Resolved_TaskId_Has_No_DeepLink_Fallback()
    {
        var employeeId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var reader = new FakeOnboardingReportReader(
        [
            BuildItem(employeeId, new OnboardingReportTaskItem("Set up laptop", null, "HR", false, Guid.NewGuid())),
        ]);

        var provider = new OutstandingOnboardingTasksWorkloadActionProvider(
            reader, new FakeDirectReportsReader(), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-hr"), new FakeOpenTaskBySourceEntityReader(), new FakeCurrentUser(callerId));

        var result = await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(callerId), WorkloadScope.Hr, CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Null(action.TaskId);
        Assert.Equal("", action.DeepLinkUrl);
    }

    [Fact]
    public async Task Multiple_Outstanding_Tasks_With_The_Same_Title_Each_Resolve_Their_Own_Distinct_TaskId()
    {
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var onboardingTaskId1 = Guid.NewGuid();
        var onboardingTaskId2 = Guid.NewGuid();
        var linkedTaskId1 = Guid.NewGuid();
        var linkedTaskId2 = Guid.NewGuid();

        var reader = new FakeOnboardingReportReader(
        [
            BuildItem(employeeA, new OnboardingReportTaskItem("Set up laptop", null, "HR", false, onboardingTaskId1)),
            BuildItem(employeeB, new OnboardingReportTaskItem("Set up laptop", null, "HR", false, onboardingTaskId2)),
        ]);

        var provider = new OutstandingOnboardingTasksWorkloadActionProvider(
            reader, new FakeDirectReportsReader(), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-hr"),
            new FakeOpenTaskBySourceEntityReader(new Dictionary<Guid, Guid>
            {
                [onboardingTaskId1] = linkedTaskId1,
                [onboardingTaskId2] = linkedTaskId2,
            }),
            new FakeCurrentUser(callerId));

        var result = await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(callerId), WorkloadScope.Hr, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, a => a.EmployeeId == employeeA && a.TaskId == linkedTaskId1);
        Assert.Contains(result, a => a.EmployeeId == employeeB && a.TaskId == linkedTaskId2);
        Assert.NotEqual(
            result.Single(a => a.EmployeeId == employeeA).TaskId,
            result.Single(a => a.EmployeeId == employeeB).TaskId);
    }

    [Fact]
    public async Task Overdue_Task_Maps_To_Overdue_Status()
    {
        var employeeId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var reader = new FakeOnboardingReportReader(
        [
            BuildItem(employeeId, new OnboardingReportTaskItem("Late task", new DateOnly(2026, 1, 1), "HR", true)),
        ]);

        var provider = new OutstandingOnboardingTasksWorkloadActionProvider(
            reader, new FakeDirectReportsReader(), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-hr"), new FakeOpenTaskBySourceEntityReader(), new FakeCurrentUser(callerId));

        var result = await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(callerId), WorkloadScope.Hr, CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal("Overdue", action.Status);
    }

    [Fact]
    public async Task DualHrAndManagerCaller_Requesting_ManagerScope_Sees_Only_TeamScoped_Results_Not_CompanyWide()
    {
        var directReportId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var reader = new FakeOnboardingReportReader(
        [
            BuildItem(directReportId, new OnboardingReportTaskItem("Task A", null, "Manager", false)),
            BuildItem(otherEmployeeId, new OnboardingReportTaskItem("Task B", null, "Manager", false)),
        ]);

        var provider = new OutstandingOnboardingTasksWorkloadActionProvider(
            reader, new FakeDirectReportsReader([directReportId]), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-hr", "reporting:view-onboarding"),
            new FakeOpenTaskBySourceEntityReader(), new FakeCurrentUser(callerId));

        var result = await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(callerId), WorkloadScope.Manager, CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal(directReportId, action.EmployeeId);
    }

    [Fact]
    public async Task DualHrAndManagerCaller_Requesting_HrScope_Sees_CompanyWide_Results()
    {
        var directReportId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var reader = new FakeOnboardingReportReader(
        [
            BuildItem(directReportId, new OnboardingReportTaskItem("Task A", null, "Manager", false)),
            BuildItem(otherEmployeeId, new OnboardingReportTaskItem("Task B", null, "Manager", false)),
        ]);

        var provider = new OutstandingOnboardingTasksWorkloadActionProvider(
            reader, new FakeDirectReportsReader([directReportId]), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-hr", "reporting:view-onboarding"),
            new FakeOpenTaskBySourceEntityReader(), new FakeCurrentUser(callerId));

        var result = await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(callerId), WorkloadScope.Hr, CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task Caller_Without_ViewHr_Requesting_HrScope_Returns_Empty_Workspace_Param_Cannot_Grant_Access()
    {
        var callerId = Guid.NewGuid();
        var reader = new FakeOnboardingReportReader(
        [
            BuildItem(Guid.NewGuid(), new OnboardingReportTaskItem("Task A", null, "Manager", false)),
        ]);

        var provider = new OutstandingOnboardingTasksWorkloadActionProvider(
            reader, new FakeDirectReportsReader([Guid.NewGuid()]), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-onboarding"), new FakeOpenTaskBySourceEntityReader(), new FakeCurrentUser(callerId));

        var result = await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(callerId), WorkloadScope.Hr, CancellationToken.None);

        Assert.Empty(result);
    }

    // ── Manager workspace: only tasks the manager may actually open are actionable ─────────────
    // GetTask authorizes only the task's assignee, a manager anywhere in the ASSIGNEE's reporting
    // line, or an HR Administrator (unassigned tasks are HR-only). A manager's queue row must not
    // offer "Open task" for a task GetTask will then reject.

    private static OutstandingOnboardingTasksWorkloadActionProvider ManagerProvider(
        Guid callerId, Guid reportId, Guid onboardingTaskId, Guid linkedTaskId, Guid? assignee) =>
        new(
            new FakeOnboardingReportReader(
            [
                BuildItem(reportId, new OnboardingReportTaskItem("Set up workstation", null, "Unassigned", true, onboardingTaskId)),
            ]),
            new FakeDirectReportsReader([reportId]),
            new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-onboarding"),
            new FakeOpenTaskBySourceEntityReader(
                new Dictionary<Guid, Guid> { [onboardingTaskId] = linkedTaskId },
                new Dictionary<Guid, Guid?> { [linkedTaskId] = assignee }),
            new FakeCurrentUser(callerId));

    [Fact]
    public async Task ManagerScope_UnassignedTask_IsShownButNotOwnerActionable()
    {
        var callerId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var onboardingTaskId = Guid.NewGuid();
        var linkedTaskId = Guid.NewGuid();

        var provider = ManagerProvider(callerId, reportId, onboardingTaskId, linkedTaskId, assignee: null);

        var result = await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(callerId), WorkloadScope.Manager, CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal(linkedTaskId, action.TaskId);
        Assert.False(action.IsOwnerActionable);
        Assert.Equal("Unassigned — owned by HR", action.OwnerLabel);
    }

    [Fact]
    public async Task ManagerScope_TaskAssignedOutsideTeam_IsNotOwnerActionable()
    {
        var callerId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var onboardingTaskId = Guid.NewGuid();
        var linkedTaskId = Guid.NewGuid();

        var provider = ManagerProvider(callerId, reportId, onboardingTaskId, linkedTaskId, assignee: Guid.NewGuid());

        var result = await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(callerId), WorkloadScope.Manager, CancellationToken.None);

        var action = Assert.Single(result);
        Assert.False(action.IsOwnerActionable);
        Assert.Equal("Assigned outside your team", action.OwnerLabel);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ManagerScope_TaskAssignedToManagerOrReport_IsOwnerActionable(bool assignedToManager)
    {
        var callerId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var onboardingTaskId = Guid.NewGuid();
        var linkedTaskId = Guid.NewGuid();

        var provider = ManagerProvider(callerId, reportId, onboardingTaskId, linkedTaskId,
            assignee: assignedToManager ? callerId : reportId);

        var result = await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(callerId), WorkloadScope.Manager, CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal(linkedTaskId, action.TaskId);
        Assert.True(action.IsOwnerActionable);
        Assert.Null(action.OwnerLabel);
    }

    [Fact]
    public async Task HrScope_UnassignedTask_RemainsOwnerActionable()
    {
        var callerId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var onboardingTaskId = Guid.NewGuid();
        var linkedTaskId = Guid.NewGuid();

        var provider = new OutstandingOnboardingTasksWorkloadActionProvider(
            new FakeOnboardingReportReader(
            [
                BuildItem(employeeId, new OnboardingReportTaskItem("Set up workstation", null, "Unassigned", false, onboardingTaskId)),
            ]),
            new FakeDirectReportsReader(), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-hr"),
            new FakeOpenTaskBySourceEntityReader(
                new Dictionary<Guid, Guid> { [onboardingTaskId] = linkedTaskId },
                new Dictionary<Guid, Guid?> { [linkedTaskId] = null }),
            new FakeCurrentUser(callerId));

        var result = await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(callerId), WorkloadScope.Hr, CancellationToken.None);

        var action = Assert.Single(result);
        Assert.True(action.IsOwnerActionable);
        Assert.Null(action.OwnerLabel);
    }
}
