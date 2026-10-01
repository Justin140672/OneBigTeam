using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Services;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class LeaveApprovalsHrOwnershipTests
{
    private static LeaveDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ClaimsPrincipal CallerWithSub(Guid employeeId) =>
        new(new ClaimsIdentity([new Claim("sub", employeeId.ToString())]));

    private static LeaveRequest PendingRequest(Guid companyId, Guid employeeId) =>
        LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 6), LeaveDayPart.FullDay,
            4m, "Holiday", DateTimeOffset.UtcNow);

    private static async Task<WorkloadAction> HrActionAsync(Guid? assignee, Guid callerId, string? assigneeName = null)
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var request = PendingRequest(companyId, Guid.NewGuid());
        context.LeaveRequests.Add(request);
        await context.SaveChangesAsync();

        var linkedTaskId = Guid.NewGuid();
        var names = new Dictionary<Guid, EmployeeDepartmentInfo>();
        if (assignee is { } assigneeId && assigneeName is not null)
            names[assigneeId] = new EmployeeDepartmentInfo(assigneeId, assigneeName, null, null);

        var provider = new LeavePendingApprovalsWorkloadActionProvider(
            context, new FakeDirectReportsReader(), new FakeEmployeeDepartmentReader(names),
            new FakeAuthorizationService("reporting:view-hr"),
            new FakeOpenTaskBySourceEntityReader(
                new Dictionary<Guid, Guid> { [request.Id] = linkedTaskId },
                new Dictionary<Guid, Guid?> { [linkedTaskId] = assignee }),
            new FakeCurrentUser(callerId));

        return Assert.Single(await provider.GetActionsAsync(companyId, CallerWithSub(callerId), WorkloadScope.Hr, CancellationToken.None));
    }

    [Fact]
    public async Task HrScope_ManagerOwnedApproval_IsVisibilityOnly_AndNamesTheManager()
    {
        var manager = Guid.NewGuid();

        var action = await HrActionAsync(manager, callerId: Guid.NewGuid(), assigneeName: "Jordan Lee");

        Assert.Equal(WorkloadActionability.VisibilityOnly, action.Actionability);
        Assert.Equal("Assigned to Jordan Lee", action.OwnerLabel);
        Assert.Contains("Jordan Lee", action.VisibilityReason);
    }

    [Fact]
    public async Task HrScope_UserWhoIsAlsoTheManager_CanActOnTheirOwnApproval()
    {
        var hrAndManager = Guid.NewGuid();

        var action = await HrActionAsync(hrAndManager, callerId: hrAndManager);

        Assert.Equal(WorkloadActionability.CanAct, action.Actionability);
        Assert.True(action.IsOwnerActionable);
        Assert.NotNull(action.TaskId);
    }

    [Fact]
    public async Task HrScope_UnassignedApproval_IsNotAnHrActionBecauseHrDoesNotOwnManagerApprovals()
    {
        var action = await HrActionAsync(assignee: null, callerId: Guid.NewGuid());

        Assert.Equal(WorkloadActionability.VisibilityOnly, action.Actionability);
        Assert.Equal("Owned by the employee's manager", action.OwnerLabel);
    }
}
