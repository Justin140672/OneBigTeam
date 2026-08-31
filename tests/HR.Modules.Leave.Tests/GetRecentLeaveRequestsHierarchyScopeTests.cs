using System.Security.Claims;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.GetRecentLeaveRequests;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Services;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

/// <summary>
/// DSH-02: for a non-HR (manager) viewer, GetRecentLeaveRequests and the Pending Leave Approvals
/// workload provider scope to the viewer's entire reporting sub-tree (direct and indirect reports)
/// via <c>GetAllDescendantIdsAsync</c>; HR administrators keep the company-wide view. A peer /
/// unrelated manager's requests are excluded. See
/// specifications/architecture/11-manager-hierarchy-scope.md.
/// </summary>
public class GetRecentLeaveRequestsHierarchyScopeTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 9, 9, 0, 0, TimeSpan.Zero);

    private static LeaveDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ClaimsPrincipal CallerWithSub(Guid employeeId) =>
        new(new ClaimsIdentity([new Claim("sub", employeeId.ToString())]));

    private static LeaveType SeedLeaveType(LeaveDbContext db, Guid companyId)
    {
        var leaveType = LeaveType.Create(
            Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, Now);
        db.LeaveTypes.Add(leaveType);
        return leaveType;
    }

    private static LeaveRequest PendingRequest(Guid companyId, Guid employeeId, Guid leaveTypeId, DateTimeOffset createdAt) =>
        LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(),
            new DateOnly(2026, 7, 1), LeaveDayPart.FullDay,
            new DateOnly(2026, 7, 3), LeaveDayPart.FullDay,
            3m, "Trip", createdAt);

    [Fact]
    public async Task Handler_NonHr_Viewer_Includes_Indirect_Report_Excludes_Peer_Keeps_Direct()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var leaveType = SeedLeaveType(db, companyId);

        var seniorManager = Guid.NewGuid();
        var lineManager = Guid.NewGuid();
        var directReport = Guid.NewGuid();
        var indirectReport = Guid.NewGuid();
        var peerReport = Guid.NewGuid();

        var reader = FakeDirectReportsReader.WithHierarchy(
            (seniorManager, lineManager),
            (seniorManager, directReport),
            (lineManager, indirectReport),
            (Guid.NewGuid(), peerReport));

        var directReq = PendingRequest(companyId, directReport, leaveType.Id, Now);
        var indirectReq = PendingRequest(companyId, indirectReport, leaveType.Id, Now.AddMinutes(1));
        var peerReq = PendingRequest(companyId, peerReport, leaveType.Id, Now.AddMinutes(2));
        db.LeaveRequests.AddRange(directReq, indirectReq, peerReq);
        await db.SaveChangesAsync();

        var handler = new GetRecentLeaveRequestsHandler(
            db, new FakeEmployeeNameReader(), reader, new FakeOpenTaskBySourceEntityReader(), new FakeClock(Now.UtcDateTime));

        var result = await handler.HandleAsync(
            new GetRecentLeaveRequestsRequest(companyId, null), seniorManager, isHrAdministrator: false, CancellationToken.None);

        var ids = result.Items.Select(i => i.LeaveRequestId).ToHashSet();
        Assert.Contains(directReq.Id, ids);
        Assert.Contains(indirectReq.Id, ids);
        Assert.DoesNotContain(peerReq.Id, ids);
    }

    [Fact]
    public async Task Handler_HrAdministrator_Still_Sees_Company_Wide()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var leaveType = SeedLeaveType(db, companyId);

        db.LeaveRequests.AddRange(
            PendingRequest(companyId, Guid.NewGuid(), leaveType.Id, Now),
            PendingRequest(companyId, Guid.NewGuid(), leaveType.Id, Now.AddMinutes(1)));
        await db.SaveChangesAsync();

        var handler = new GetRecentLeaveRequestsHandler(
            db, new FakeEmployeeNameReader(), FakeDirectReportsReader.WithHierarchy(),
            new FakeOpenTaskBySourceEntityReader(), new FakeClock(Now.UtcDateTime));

        var result = await handler.HandleAsync(
            new GetRecentLeaveRequestsRequest(companyId, null), Guid.NewGuid(), isHrAdministrator: true, CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task PendingApprovalsProvider_Manager_Includes_Indirect_Report_Excludes_Peer()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var leaveType = SeedLeaveType(db, companyId);

        var manager = Guid.NewGuid();
        var lineManager = Guid.NewGuid();
        var indirectReport = Guid.NewGuid();
        var peerReport = Guid.NewGuid();

        var reader = FakeDirectReportsReader.WithHierarchy(
            (manager, lineManager),
            (lineManager, indirectReport),
            (Guid.NewGuid(), peerReport));

        db.LeaveRequests.AddRange(
            PendingRequest(companyId, indirectReport, leaveType.Id, Now),
            PendingRequest(companyId, peerReport, leaveType.Id, Now.AddMinutes(1)));
        await db.SaveChangesAsync();

        var provider = new LeavePendingApprovalsWorkloadActionProvider(
            db, reader, new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService(), new FakeCurrentUser(manager));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(manager), CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal(indirectReport, action.EmployeeId);
    }

    [Fact]
    public async Task PendingApprovalsProvider_HrCaller_Still_Sees_Company_Wide()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var leaveType = SeedLeaveType(db, companyId);
        var caller = Guid.NewGuid();

        db.LeaveRequests.AddRange(
            PendingRequest(companyId, Guid.NewGuid(), leaveType.Id, Now),
            PendingRequest(companyId, Guid.NewGuid(), leaveType.Id, Now.AddMinutes(1)));
        await db.SaveChangesAsync();

        var provider = new LeavePendingApprovalsWorkloadActionProvider(
            db, FakeDirectReportsReader.WithHierarchy(), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-hr"), new FakeCurrentUser(caller));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(caller), CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task PendingApprovalsProvider_NonManager_With_Empty_Subtree_Is_Empty()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var leaveType = SeedLeaveType(db, companyId);
        var caller = Guid.NewGuid();

        db.LeaveRequests.Add(PendingRequest(companyId, Guid.NewGuid(), leaveType.Id, Now));
        await db.SaveChangesAsync();

        var provider = new LeavePendingApprovalsWorkloadActionProvider(
            db, FakeDirectReportsReader.WithHierarchy(), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService(), new FakeCurrentUser(caller));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(caller), CancellationToken.None);

        Assert.Empty(result);
    }
}
