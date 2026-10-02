using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Services;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class LeaveDualRoleOwnershipTests
{
    private static LeaveDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ClaimsPrincipal CallerWithSub(Guid employeeId) =>
        new(new ClaimsIdentity([new Claim("sub", employeeId.ToString())]));

    private static async Task<(IReadOnlyList<WorkloadAction> Actions, Guid CompanyId, Guid RequestId)> RunAsync(
        WorkloadScope scope,
        Guid viewer,
        Guid employee,
        bool viewerIsManager,
        bool hasTask,
        Guid? taskAssignee)
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var request = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employee, Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 6), LeaveDayPart.FullDay,
            4m, "Holiday", DateTimeOffset.UtcNow);
        context.LeaveRequests.Add(request);
        await context.SaveChangesAsync();

        var taskId = Guid.NewGuid();
        var taskReader = hasTask
            ? new FakeOpenTaskBySourceEntityReader(
                new Dictionary<Guid, Guid> { [request.Id] = taskId },
                new Dictionary<Guid, Guid?> { [taskId] = taskAssignee })
            : new FakeOpenTaskBySourceEntityReader();

        var provider = new LeavePendingApprovalsWorkloadActionProvider(
            context,
            viewerIsManager ? new FakeDirectReportsReader(employee) : new FakeDirectReportsReader(),
            new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-hr"),
            taskReader,
            new FakeCurrentUser(viewer));

        var actions = await provider.GetActionsAsync(companyId, CallerWithSub(viewer), scope, CancellationToken.None);
        return (actions, companyId, request.Id);
    }

    [Theory]
    [InlineData(WorkloadScope.Hr)]
    [InlineData(WorkloadScope.Manager)]
    public async Task HrAndManager_RequestWithoutOpenTask_IsActionableInBothScopes_WithAReachableDestination(WorkloadScope scope)
    {
        var viewer = Guid.NewGuid();
        var employee = Guid.NewGuid();

        var (actions, companyId, _) = await RunAsync(scope, viewer, employee, viewerIsManager: true, hasTask: false, taskAssignee: null);

        var action = Assert.Single(actions);
        Assert.Equal(WorkloadActionability.CanAct, action.Actionability);
        Assert.Equal($"/companies/{companyId}/employees/{employee}?tab=leave", action.DeepLinkUrl);
        Assert.Null(action.OwnerLabel);
    }

    [Fact]
    public async Task HrScope_UnassignedTask_ForViewersOwnDirectReport_IsActionableNotWaitingOnOthers()
    {
        var viewer = Guid.NewGuid();
        var employee = Guid.NewGuid();

        var (actions, _, _) = await RunAsync(WorkloadScope.Hr, viewer, employee, viewerIsManager: true, hasTask: true, taskAssignee: null);

        var action = Assert.Single(actions);
        Assert.Equal(WorkloadActionability.CanAct, action.Actionability);
        Assert.NotNull(action.TaskId);
    }

    [Fact]
    public async Task HrScope_TaskAssignedToAnotherManager_StaysVisibilityOnly_EvenForHrAndManager()
    {
        var viewer = Guid.NewGuid();
        var employee = Guid.NewGuid();

        var (actions, _, _) = await RunAsync(WorkloadScope.Hr, viewer, employee, viewerIsManager: false, hasTask: true, taskAssignee: Guid.NewGuid());

        var action = Assert.Single(actions);
        Assert.Equal(WorkloadActionability.VisibilityOnly, action.Actionability);
    }

    [Fact]
    public async Task HrScope_ViewerWhoIsNotTheEmployeesManager_StillSeesUnassignedRequestAsWaitingOnOthers()
    {
        var (actions, _, _) = await RunAsync(WorkloadScope.Hr, Guid.NewGuid(), Guid.NewGuid(), viewerIsManager: false, hasTask: false, taskAssignee: null);

        var action = Assert.Single(actions);
        Assert.Equal(WorkloadActionability.VisibilityOnly, action.Actionability);
        Assert.Equal("Owned by the employee's manager", action.OwnerLabel);
    }

    [Fact]
    public async Task OwnershipIsConsistentAcrossHrAndManagerScopes_ForTheSameRequest()
    {
        var viewer = Guid.NewGuid();
        var employee = Guid.NewGuid();

        var (hr, _, _) = await RunAsync(WorkloadScope.Hr, viewer, employee, viewerIsManager: true, hasTask: true, taskAssignee: viewer);
        var (manager, _, _) = await RunAsync(WorkloadScope.Manager, viewer, employee, viewerIsManager: true, hasTask: true, taskAssignee: viewer);

        Assert.Equal(Assert.Single(hr).Actionability, Assert.Single(manager).Actionability);
        Assert.Equal(WorkloadActionability.CanAct, Assert.Single(hr).Actionability);
    }
}
