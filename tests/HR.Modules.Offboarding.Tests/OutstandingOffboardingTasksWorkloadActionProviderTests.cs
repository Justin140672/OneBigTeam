using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Offboarding.Services;
using HR.Modules.Offboarding.Tests.Infrastructure;
using HR.Modules.Tasks.Contracts;

namespace HR.Modules.Offboarding.Tests;

public class OutstandingOffboardingTasksWorkloadActionProviderTests
{
    private static readonly DateOnly Today = new(2026, 7, 29);

    private static ClaimsPrincipal CallerWithSub(Guid employeeId) =>
        new(new ClaimsIdentity([new Claim("sub", employeeId.ToString())]));

    private static OffboardingReportItem BuildItem(
        Guid employeeId, DateOnly lastWorkingDay, params string[] outstandingTaskTitles) =>
        new(employeeId, lastWorkingDay, "InProgress", outstandingTaskTitles.Length, 0,
            outstandingTaskTitles, [], DocumentsReturned: false,
            OutstandingTaskIds: outstandingTaskTitles.Select(_ => Guid.NewGuid()).ToList());

    [Fact]
    public async Task HrCaller_Sees_All_Outstanding_Offboarding_Tasks_CompanyWide()
    {
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();
        var reader = new FakeOffboardingReportReader(
        [
            BuildItem(employeeA, Today.AddDays(5), "Return laptop"),
            BuildItem(employeeB, Today.AddDays(10), "Exit interview"),
        ]);

        var provider = new OutstandingOffboardingTasksWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"),
            new FakeOpenTaskBySourceEntityReader());

        var result = await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(Guid.NewGuid()), WorkloadScope.Hr, CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task NonHrCaller_Returns_Empty_Even_With_Manager_Role()
    {
        var reader = new FakeOffboardingReportReader(
        [
            BuildItem(Guid.NewGuid(), Today.AddDays(5), "Return laptop"),
        ]);

        var provider = new OutstandingOffboardingTasksWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-onboarding"),
            new FakeOpenTaskBySourceEntityReader());

        var result = await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(Guid.NewGuid()), WorkloadScope.Hr, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task CallerWithNoRole_Returns_Empty_Not_Throws()
    {
        var reader = new FakeOffboardingReportReader(
        [
            BuildItem(Guid.NewGuid(), Today.AddDays(5), "Return laptop"),
        ]);

        var provider = new OutstandingOffboardingTasksWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService(),
            new FakeOpenTaskBySourceEntityReader());

        var result = await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(Guid.NewGuid()), WorkloadScope.Hr, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Employees_With_No_Outstanding_Tasks_Are_Excluded()
    {
        var reader = new FakeOffboardingReportReader(
        [
            BuildItem(Guid.NewGuid(), Today.AddDays(5)),
        ]);

        var provider = new OutstandingOffboardingTasksWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"),
            new FakeOpenTaskBySourceEntityReader());

        var result = await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(Guid.NewGuid()), WorkloadScope.Hr, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Status_Is_Outstanding_Not_Overdue_When_LastWorkingDay_Is_Exactly_Today()
    {
        var actualToday = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var employeeId = Guid.NewGuid();
        var reader = new FakeOffboardingReportReader(
        [
            BuildItem(employeeId, actualToday, "Return laptop"),
        ]);

        var provider = new OutstandingOffboardingTasksWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"),
            new FakeOpenTaskBySourceEntityReader());

        var result = await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(Guid.NewGuid()), WorkloadScope.Hr, CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal("Outstanding", action.Status);
    }

    [Fact]
    public async Task Maps_ActionType_Category_And_Overdue_Status_No_EmployeeProfile_Fallback()
    {
        var employeeId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var pastDueDate = Today.AddDays(-3);
        var reader = new FakeOffboardingReportReader(
        [
            BuildItem(employeeId, pastDueDate, "Return laptop"),
        ]);

        var provider = new OutstandingOffboardingTasksWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"),
            new FakeOpenTaskBySourceEntityReader());

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), WorkloadScope.Hr, CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal("Return laptop", action.ActionType);
        Assert.Equal("Outstanding Offboarding Tasks", action.ActionCategory);
        Assert.Equal("Overdue", action.Status);
        Assert.Equal(pastDueDate, action.DueDate);
        Assert.Equal("", action.DeepLinkUrl);
        Assert.Null(action.TaskId);
    }

    [Fact]
    public async Task Resolves_Exact_Linked_Task_Per_Offboarding_Task()
    {
        var employeeId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var offboardingTaskId = Guid.NewGuid();
        var linkedTaskId = Guid.NewGuid();
        var reader = new FakeOffboardingReportReader(
        [
            new OffboardingReportItem(
                employeeId, Today.AddDays(5), "InProgress", 1, 0,
                ["Return laptop"], [], DocumentsReturned: false, OutstandingTaskIds: [offboardingTaskId]),
        ]);

        var provider = new OutstandingOffboardingTasksWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"),
            new FakeOpenTaskBySourceEntityReader(new Dictionary<Guid, Guid> { [offboardingTaskId] = linkedTaskId }));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), WorkloadScope.Hr, CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal(linkedTaskId, action.TaskId);
    }

    [Fact]
    public async Task Multiple_Outstanding_Tasks_With_The_Same_Title_Each_Resolve_Their_Own_Distinct_TaskId()
    {
        // Two offboarding tasks that share a title (e.g. two employees both have "Return laptop")
        // must not have their linked task resolved by title/employee matching — each source
        // OffboardingTask id must be keyed independently.
        var employeeId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var offboardingTaskId1 = Guid.NewGuid();
        var offboardingTaskId2 = Guid.NewGuid();
        var linkedTaskId1 = Guid.NewGuid();
        var linkedTaskId2 = Guid.NewGuid();

        var reader = new FakeOffboardingReportReader(
        [
            new OffboardingReportItem(
                employeeId, Today.AddDays(5), "InProgress", 2, 0,
                ["Return laptop", "Return laptop"], [], DocumentsReturned: false,
                OutstandingTaskIds: [offboardingTaskId1, offboardingTaskId2]),
        ]);

        var provider = new OutstandingOffboardingTasksWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"),
            new FakeOpenTaskBySourceEntityReader(new Dictionary<Guid, Guid>
            {
                [offboardingTaskId1] = linkedTaskId1,
                [offboardingTaskId2] = linkedTaskId2,
            }));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), WorkloadScope.Hr, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, a => a.TaskId == linkedTaskId1);
        Assert.Contains(result, a => a.TaskId == linkedTaskId2);
        Assert.NotEqual(result[0].TaskId, result[1].TaskId);
    }

    [Fact]
    public async Task HrCaller_Requesting_ManagerScope_Returns_Empty_HrOnly_Category_Never_Leaks_Into_Manager_Workspace()
    {
        var reader = new FakeOffboardingReportReader(
        [
            BuildItem(Guid.NewGuid(), Today.AddDays(5), "Return laptop"),
        ]);

        var provider = new OutstandingOffboardingTasksWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"),
            new FakeOpenTaskBySourceEntityReader());

        var result = await provider.GetActionsAsync(Guid.NewGuid(), CallerWithSub(Guid.NewGuid()), WorkloadScope.Manager, CancellationToken.None);

        Assert.Empty(result);
    }
}
