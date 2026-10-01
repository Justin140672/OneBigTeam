using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.Modules.Tasks.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Tests;

public class ManagerTasksOverdueHrScopeOwnershipTests
{
    private static TasksDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<TasksDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ClaimsPrincipal CallerWithSub(Guid employeeId) =>
        new(new ClaimsIdentity([new Claim("sub", employeeId.ToString())]));

    private static TaskItem OverdueTask(Guid companyId, Guid assignedEmployeeId, string title) =>
        TaskItem.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), title, null,
            TaskPriority.Medium, TaskSource.Workflow, TaskActionType.Complete,
            DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-3),
            assignedEmployeeId, null, DateTimeOffset.UtcNow);

    private static ManagerTasksOverdueWorkloadActionProvider Provider(
        TasksDbContext context, Guid callerId, IReadOnlyDictionary<Guid, EmployeeDepartmentInfo>? names = null) =>
        new(context, new FakeDirectReportsReader(), new FakeEmployeeDepartmentReader(names),
            new FakeAuthorizationService("reporting:view-hr"), new FakeCurrentUser(callerId));

    [Fact]
    public async Task HrScope_TasksAssignedToOtherEmployees_AreVisibilityOnly_WithResponsiblePersonNamed()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        context.TaskItems.Add(OverdueTask(companyId, otherId, "Send fit note"));
        await context.SaveChangesAsync();

        var names = new Dictionary<Guid, EmployeeDepartmentInfo> { [otherId] = new(otherId, "Sam Taylor", null, "Ops") };
        var result = await Provider(context, callerId, names)
            .GetActionsAsync(companyId, CallerWithSub(callerId), WorkloadScope.Hr, CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal(WorkloadActionability.VisibilityOnly, action.Actionability);
        Assert.Equal("Assigned to Sam Taylor", action.OwnerLabel);
        Assert.Contains("monitor", action.VisibilityReason);
    }

    [Fact]
    public async Task HrScope_TasksAssignedToTheCaller_AreExcluded_BecauseTheEmployeeProviderOwnsThem()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        context.TaskItems.Add(OverdueTask(companyId, callerId, "My own overdue task"));
        await context.SaveChangesAsync();

        var result = await Provider(context, callerId)
            .GetActionsAsync(companyId, CallerWithSub(callerId), WorkloadScope.Hr, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task HrScope_CompletedOrCancelledTasks_AreNotInEitherQueue()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var completed = OverdueTask(companyId, Guid.NewGuid(), "Completed");
        completed.Complete(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var cancelled = OverdueTask(companyId, Guid.NewGuid(), "Cancelled");
        cancelled.Cancel(DateTimeOffset.UtcNow);
        context.TaskItems.AddRange(completed, cancelled);
        await context.SaveChangesAsync();

        var result = await Provider(context, callerId)
            .GetActionsAsync(companyId, CallerWithSub(callerId), WorkloadScope.Hr, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ManagerScope_TeamTasks_RemainActionable()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        context.TaskItems.Add(OverdueTask(companyId, reportId, "Team task"));
        await context.SaveChangesAsync();

        var provider = new ManagerTasksOverdueWorkloadActionProvider(
            context, new FakeDirectReportsReader([reportId]), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService(), new FakeCurrentUser(managerId));

        var action = Assert.Single(await provider.GetActionsAsync(
            companyId, CallerWithSub(managerId), WorkloadScope.Manager, CancellationToken.None));

        Assert.Equal(WorkloadActionability.CanAct, action.Actionability);
    }
}
