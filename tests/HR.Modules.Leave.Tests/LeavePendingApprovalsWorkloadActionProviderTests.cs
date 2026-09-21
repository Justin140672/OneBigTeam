using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Services;
using HR.Modules.Leave.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

/// <summary>
/// OBT-721 workload action provider tests for pending leave approvals — mirrors the row-scoping
/// coverage pattern established by GetProbationReportHandlerTests (HR sees company-wide, Manager is
/// scoped to direct reports, Manager with no direct reports and non-HR/non-Manager callers get an
/// empty list rather than an exception or company-wide data).
/// </summary>
public class LeavePendingApprovalsWorkloadActionProviderTests
{
    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LeaveDbContext(options);
    }

    private static ClaimsPrincipal CallerWithSub(Guid employeeId) =>
        new(new ClaimsIdentity([new Claim("sub", employeeId.ToString())]));

    private static LeaveRequest CreatePendingRequest(Guid companyId, Guid employeeId, DateOnly startDate) =>
        LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(), Guid.NewGuid(),
            startDate, LeaveDayPart.FullDay,
            startDate.AddDays(3), LeaveDayPart.FullDay,
            4m, "Holiday", DateTimeOffset.UtcNow);

    [Fact]
    public async Task GetActionsAsync_HrCaller_Sees_All_Pending_Requests_CompanyWide()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();
        var callerId = Guid.NewGuid();

        context.LeaveRequests.AddRange(
            CreatePendingRequest(companyId, employeeA, new DateOnly(2026, 8, 3)),
            CreatePendingRequest(companyId, employeeB, new DateOnly(2026, 8, 10)));
        await context.SaveChangesAsync();

        var provider = new LeavePendingApprovalsWorkloadActionProvider(
            context, new FakeDirectReportsReader(), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-hr"), new FakeOpenTaskBySourceEntityReader(), new FakeCurrentUser(callerId));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(callerId), WorkloadScope.Hr, CancellationToken.None);

        Assert.Equal(2, result.Count);
        // A pending leave request's approval task is always owned by the employee's manager — HR
        // only ever sees these rows for oversight, never as something to click into and action.
        Assert.All(result, a => Assert.False(a.IsOwnerActionable));
        Assert.All(result, a => Assert.Equal("Owned by the employee's manager", a.OwnerLabel));
    }

    [Fact]
    public async Task GetActionsAsync_ManagerCaller_Is_Scoped_To_DirectReports_Only()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var directReportId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var callerEmployeeId = Guid.NewGuid();

        context.LeaveRequests.AddRange(
            CreatePendingRequest(companyId, directReportId, new DateOnly(2026, 8, 3)),
            CreatePendingRequest(companyId, otherEmployeeId, new DateOnly(2026, 8, 10)));
        await context.SaveChangesAsync();

        var provider = new LeavePendingApprovalsWorkloadActionProvider(
            context, new FakeDirectReportsReader([directReportId]), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService(), new FakeOpenTaskBySourceEntityReader(), new FakeCurrentUser(callerEmployeeId));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(callerEmployeeId), WorkloadScope.Manager, CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal(directReportId, action.EmployeeId);
        // The manager is the true owner of the approval task in the Manager workspace.
        Assert.True(action.IsOwnerActionable);
        Assert.Null(action.OwnerLabel);
    }

    [Fact]
    public async Task GetActionsAsync_ManagerWithNoDirectReports_Returns_Empty()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var callerId = Guid.NewGuid();
        context.LeaveRequests.Add(CreatePendingRequest(companyId, Guid.NewGuid(), new DateOnly(2026, 8, 3)));
        await context.SaveChangesAsync();

        var provider = new LeavePendingApprovalsWorkloadActionProvider(
            context, new FakeDirectReportsReader([]), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService(), new FakeOpenTaskBySourceEntityReader(), new FakeCurrentUser(callerId));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(callerId), WorkloadScope.Manager, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetActionsAsync_CallerWithNoRecognisedRole_Returns_Empty_Not_Throws()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        context.LeaveRequests.Add(CreatePendingRequest(companyId, Guid.NewGuid(), new DateOnly(2026, 8, 3)));
        await context.SaveChangesAsync();

        var provider = new LeavePendingApprovalsWorkloadActionProvider(
            context, new FakeDirectReportsReader([]), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService(), new FakeOpenTaskBySourceEntityReader(), new FakeCurrentUser(null));

        // No resolved current-user id at all — the caller can't even be resolved to an employee id.
        var result = await provider.GetActionsAsync(companyId, new ClaimsPrincipal(new ClaimsIdentity()), WorkloadScope.Manager, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetActionsAsync_Maps_ActionType_Category_DueDate_And_DeepLink()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        var startDate = new DateOnly(2026, 8, 3);

        context.LeaveRequests.Add(CreatePendingRequest(companyId, employeeId, startDate));
        await context.SaveChangesAsync();

        var provider = new LeavePendingApprovalsWorkloadActionProvider(
            context, new FakeDirectReportsReader(), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-hr"), new FakeOpenTaskBySourceEntityReader(), new FakeCurrentUser(callerId));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(callerId), WorkloadScope.Hr, CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal("Approve Leave Request", action.ActionType);
        Assert.Equal("Pending Leave Approvals", action.ActionCategory);
        Assert.Equal(startDate, action.DueDate);
        // No employee-profile fallback: this category is entirely task-backed.
        Assert.Equal("", action.DeepLinkUrl);
        Assert.Equal("Pending", action.Status);
    }

    [Fact]
    public async Task GetActionsAsync_DualHrAndManagerCaller_Requesting_ManagerScope_Sees_Only_TeamScoped_Results()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var directReportId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var callerEmployeeId = Guid.NewGuid();

        context.LeaveRequests.AddRange(
            CreatePendingRequest(companyId, directReportId, new DateOnly(2026, 8, 3)),
            CreatePendingRequest(companyId, otherEmployeeId, new DateOnly(2026, 8, 10)));
        await context.SaveChangesAsync();

        var provider = new LeavePendingApprovalsWorkloadActionProvider(
            context, new FakeDirectReportsReader([directReportId]), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-hr"), new FakeOpenTaskBySourceEntityReader(), new FakeCurrentUser(callerEmployeeId));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(callerEmployeeId), WorkloadScope.Manager, CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal(directReportId, action.EmployeeId);
    }

    [Fact]
    public async Task GetActionsAsync_DualHrAndManagerCaller_Requesting_HrScope_Sees_CompanyWide_Results()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var directReportId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var callerEmployeeId = Guid.NewGuid();

        context.LeaveRequests.AddRange(
            CreatePendingRequest(companyId, directReportId, new DateOnly(2026, 8, 3)),
            CreatePendingRequest(companyId, otherEmployeeId, new DateOnly(2026, 8, 10)));
        await context.SaveChangesAsync();

        var provider = new LeavePendingApprovalsWorkloadActionProvider(
            context, new FakeDirectReportsReader([directReportId]), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-hr"), new FakeOpenTaskBySourceEntityReader(), new FakeCurrentUser(callerEmployeeId));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(callerEmployeeId), WorkloadScope.Hr, CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetActionsAsync_Resolves_Linked_TaskId_In_HrScope_And_Marks_Not_OwnerActionable()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var callerId = Guid.NewGuid();

        var request = CreatePendingRequest(companyId, employeeId, new DateOnly(2026, 8, 3));
        context.LeaveRequests.Add(request);
        await context.SaveChangesAsync();

        var linkedTaskId = Guid.NewGuid();
        var provider = new LeavePendingApprovalsWorkloadActionProvider(
            context, new FakeDirectReportsReader(), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-hr"),
            new FakeOpenTaskBySourceEntityReader(new Dictionary<Guid, Guid> { [request.Id] = linkedTaskId }),
            new FakeCurrentUser(callerId));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(callerId), WorkloadScope.Hr, CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal(linkedTaskId, action.TaskId);
        Assert.False(action.IsOwnerActionable);
        Assert.Equal("Owned by the employee's manager", action.OwnerLabel);
    }

    [Fact]
    public async Task GetActionsAsync_Resolves_Linked_TaskId_In_ManagerScope_And_Marks_OwnerActionable()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var directReportId = Guid.NewGuid();
        var callerEmployeeId = Guid.NewGuid();

        var request = CreatePendingRequest(companyId, directReportId, new DateOnly(2026, 8, 3));
        context.LeaveRequests.Add(request);
        await context.SaveChangesAsync();

        var linkedTaskId = Guid.NewGuid();
        var provider = new LeavePendingApprovalsWorkloadActionProvider(
            context, new FakeDirectReportsReader([directReportId]), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService(),
            new FakeOpenTaskBySourceEntityReader(new Dictionary<Guid, Guid> { [request.Id] = linkedTaskId }),
            new FakeCurrentUser(callerEmployeeId));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(callerEmployeeId), WorkloadScope.Manager, CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal(linkedTaskId, action.TaskId);
        Assert.True(action.IsOwnerActionable);
        Assert.Null(action.OwnerLabel);
    }

    [Fact]
    public async Task GetActionsAsync_CallerWithoutViewHr_Requesting_HrScope_Returns_Empty()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        context.LeaveRequests.Add(CreatePendingRequest(companyId, Guid.NewGuid(), new DateOnly(2026, 8, 3)));
        await context.SaveChangesAsync();

        // Caller lacks reporting:view-hr entirely (only resolvable as a manager, if that).
        var provider = new LeavePendingApprovalsWorkloadActionProvider(
            context, new FakeDirectReportsReader(), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService(), new FakeOpenTaskBySourceEntityReader(), new FakeCurrentUser(callerId));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(callerId), WorkloadScope.Hr, CancellationToken.None);

        Assert.Empty(result);
    }
}
