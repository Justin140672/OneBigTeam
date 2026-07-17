using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.GetRecentLeaveRequests;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class GetRecentLeaveRequestsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 9, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_No_Requests_Exist()
    {
        await using var context = BuildContext();
        var handler = new GetRecentLeaveRequestsHandler(
            context, new FakeEmployeeNameReader(), new FakeDirectReportsReader(), new FakeOpenTaskBySourceEntityReader(), new FakeClock(Now.UtcDateTime));

        var result = await handler.HandleAsync(
            new GetRecentLeaveRequestsRequest(Guid.NewGuid(), null),
            Guid.NewGuid(),
            isHrAdministrator: true,
            CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_Orders_By_CreatedAt_Descending()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveType = SeedLeaveType(context, companyId);

        var earliest = CreateRequest(companyId, employeeId, leaveType.Id, Now.AddDays(-3));
        var middle = CreateRequest(companyId, employeeId, leaveType.Id, Now.AddDays(-2));
        var latest = CreateRequest(companyId, employeeId, leaveType.Id, Now.AddDays(-1));
        context.LeaveRequests.AddRange(middle, earliest, latest);
        await context.SaveChangesAsync();

        var handler = new GetRecentLeaveRequestsHandler(
            context, new FakeEmployeeNameReader(), new FakeDirectReportsReader(), new FakeOpenTaskBySourceEntityReader(), new FakeClock(Now.UtcDateTime));
        var result = await handler.HandleAsync(
            new GetRecentLeaveRequestsRequest(companyId, null),
            Guid.NewGuid(),
            isHrAdministrator: true,
            CancellationToken.None);

        Assert.Equal(
            [latest.Id, middle.Id, earliest.Id],
            result.Items.Select(i => i.LeaveRequestId).ToArray());
    }

    [Fact]
    public async Task HandleAsync_Defaults_To_Take_Ten_When_Not_Specified()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveType = SeedLeaveType(context, companyId);

        for (var i = 0; i < 12; i++)
        {
            context.LeaveRequests.Add(CreateRequest(companyId, employeeId, leaveType.Id, Now.AddDays(-i)));
        }
        await context.SaveChangesAsync();

        var handler = new GetRecentLeaveRequestsHandler(
            context, new FakeEmployeeNameReader(), new FakeDirectReportsReader(), new FakeOpenTaskBySourceEntityReader(), new FakeClock(Now.UtcDateTime));
        var result = await handler.HandleAsync(
            new GetRecentLeaveRequestsRequest(companyId, null),
            Guid.NewGuid(),
            isHrAdministrator: true,
            CancellationToken.None);

        Assert.Equal(10, result.Items.Count);
        // The 10 most recent (smallest AddDays offset -> latest CreatedAt) should be returned.
        Assert.True(result.Items.SequenceEqual(result.Items.OrderByDescending(i => i.CreatedAt)));
        Assert.Equal(Now, result.Items[0].CreatedAt);
    }

    [Fact]
    public async Task HandleAsync_Respects_Explicit_Take_Value()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveType = SeedLeaveType(context, companyId);

        for (var i = 0; i < 5; i++)
        {
            context.LeaveRequests.Add(CreateRequest(companyId, employeeId, leaveType.Id, Now.AddDays(-i)));
        }
        await context.SaveChangesAsync();

        var handler = new GetRecentLeaveRequestsHandler(
            context, new FakeEmployeeNameReader(), new FakeDirectReportsReader(), new FakeOpenTaskBySourceEntityReader(), new FakeClock(Now.UtcDateTime));
        var result = await handler.HandleAsync(
            new GetRecentLeaveRequestsRequest(companyId, 3),
            Guid.NewGuid(),
            isHrAdministrator: true,
            CancellationToken.None);

        Assert.Equal(3, result.Items.Count);
        Assert.True(result.Items.SequenceEqual(result.Items.OrderByDescending(i => i.CreatedAt)));
    }

    [Fact]
    public async Task HandleAsync_Resolves_Employee_Names_Via_EmployeeNameReader()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveType = SeedLeaveType(context, companyId);
        context.LeaveRequests.Add(CreateRequest(companyId, employeeId, leaveType.Id, Now));
        await context.SaveChangesAsync();

        var names = new Dictionary<Guid, string> { [employeeId] = "Jane Doe" };
        var handler = new GetRecentLeaveRequestsHandler(
            context, new FakeEmployeeNameReader(names), new FakeDirectReportsReader(), new FakeOpenTaskBySourceEntityReader(), new FakeClock(Now.UtcDateTime));

        var result = await handler.HandleAsync(
            new GetRecentLeaveRequestsRequest(companyId, null),
            Guid.NewGuid(),
            isHrAdministrator: true,
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("Jane Doe", item.EmployeeName);
        Assert.Equal(leaveType.Name, item.LeaveTypeName);
        Assert.Equal("Pending", item.Status);
    }

    [Fact]
    public async Task HandleAsync_Falls_Back_To_Unknown_Employee_When_Name_Not_Found()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveType = SeedLeaveType(context, companyId);
        context.LeaveRequests.Add(CreateRequest(companyId, employeeId, leaveType.Id, Now));
        await context.SaveChangesAsync();

        var handler = new GetRecentLeaveRequestsHandler(
            context, new FakeEmployeeNameReader(), new FakeDirectReportsReader(), new FakeOpenTaskBySourceEntityReader(), new FakeClock(Now.UtcDateTime));
        var result = await handler.HandleAsync(
            new GetRecentLeaveRequestsRequest(companyId, null),
            Guid.NewGuid(),
            isHrAdministrator: true,
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("Unknown Employee", item.EmployeeName);
    }

    [Fact]
    public async Task HandleAsync_Isolates_By_Company()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveType = SeedLeaveType(context, companyId);
        var otherLeaveType = SeedLeaveType(context, otherCompanyId);

        context.LeaveRequests.Add(CreateRequest(companyId, employeeId, leaveType.Id, Now));
        context.LeaveRequests.Add(CreateRequest(otherCompanyId, employeeId, otherLeaveType.Id, Now));
        await context.SaveChangesAsync();

        var handler = new GetRecentLeaveRequestsHandler(
            context, new FakeEmployeeNameReader(), new FakeDirectReportsReader(), new FakeOpenTaskBySourceEntityReader(), new FakeClock(Now.UtcDateTime));
        var result = await handler.HandleAsync(
            new GetRecentLeaveRequestsRequest(companyId, null),
            Guid.NewGuid(),
            isHrAdministrator: true,
            CancellationToken.None);

        Assert.Single(result.Items);
    }

    // ── HR-admin vs. manager scoping ──────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_NonHrViewer_Sees_Only_Direct_Reports_Requests()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var viewerId = Guid.NewGuid();
        var directReportId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var leaveType = SeedLeaveType(context, companyId);

        var reportRequest = CreateRequest(companyId, directReportId, leaveType.Id, Now);
        var otherRequest = CreateRequest(companyId, otherEmployeeId, leaveType.Id, Now);
        context.LeaveRequests.AddRange(reportRequest, otherRequest);
        await context.SaveChangesAsync();

        var handler = new GetRecentLeaveRequestsHandler(
            context, new FakeEmployeeNameReader(), new FakeDirectReportsReader(directReportId), new FakeOpenTaskBySourceEntityReader(), new FakeClock(Now.UtcDateTime));

        var result = await handler.HandleAsync(
            new GetRecentLeaveRequestsRequest(companyId, null),
            viewerId,
            isHrAdministrator: false,
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(reportRequest.Id, item.LeaveRequestId);
    }

    [Fact]
    public async Task HandleAsync_NonHrViewer_With_No_Direct_Reports_Gets_Empty_List()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var viewerId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveType = SeedLeaveType(context, companyId);

        context.LeaveRequests.Add(CreateRequest(companyId, employeeId, leaveType.Id, Now));
        await context.SaveChangesAsync();

        var handler = new GetRecentLeaveRequestsHandler(
            context, new FakeEmployeeNameReader(), new FakeDirectReportsReader(), new FakeOpenTaskBySourceEntityReader(), new FakeClock(Now.UtcDateTime));

        var result = await handler.HandleAsync(
            new GetRecentLeaveRequestsRequest(companyId, null),
            viewerId,
            isHrAdministrator: false,
            CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_NonHrViewer_Only_Sees_Pending_Requests()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var viewerId = Guid.NewGuid();
        var directReportId = Guid.NewGuid();
        var leaveType = SeedLeaveType(context, companyId);

        var pending = CreateRequest(companyId, directReportId, leaveType.Id, Now);
        var approved = CreateRequest(companyId, directReportId, leaveType.Id, Now.AddDays(-1));
        approved.Approve(Guid.NewGuid(), Now);
        context.LeaveRequests.AddRange(pending, approved);
        await context.SaveChangesAsync();

        var handler = new GetRecentLeaveRequestsHandler(
            context, new FakeEmployeeNameReader(), new FakeDirectReportsReader(directReportId), new FakeOpenTaskBySourceEntityReader(), new FakeClock(Now.UtcDateTime));

        var result = await handler.HandleAsync(
            new GetRecentLeaveRequestsRequest(companyId, null),
            viewerId,
            isHrAdministrator: false,
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(pending.Id, item.LeaveRequestId);
        Assert.Equal("Pending", item.Status);
    }

    [Fact]
    public async Task HandleAsync_HrAdministrator_Sees_All_Requests_Regardless_Of_Status_Or_Manager()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var viewerId = Guid.NewGuid();
        var unrelatedEmployeeId = Guid.NewGuid();
        var leaveType = SeedLeaveType(context, companyId);

        var pending = CreateRequest(companyId, unrelatedEmployeeId, leaveType.Id, Now);
        // Approved but not yet started (StartDate is after "Now") — still expected to show; see
        // HandleAsync_HrAdministrator_Hides_Approved_Requests_Once_Started for the other side.
        var approved = CreateRequest(
            companyId, unrelatedEmployeeId, leaveType.Id, Now.AddDays(-1),
            startDate: DateOnly.FromDateTime(Now.UtcDateTime).AddDays(5),
            endDate: DateOnly.FromDateTime(Now.UtcDateTime).AddDays(7));
        approved.Approve(Guid.NewGuid(), Now);
        context.LeaveRequests.AddRange(pending, approved);
        await context.SaveChangesAsync();

        // Viewer has no direct reports at all — HR admins must still see everything.
        var handler = new GetRecentLeaveRequestsHandler(
            context, new FakeEmployeeNameReader(), new FakeDirectReportsReader(), new FakeOpenTaskBySourceEntityReader(), new FakeClock(Now.UtcDateTime));

        var result = await handler.HandleAsync(
            new GetRecentLeaveRequestsRequest(companyId, null),
            viewerId,
            isHrAdministrator: true,
            CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_HrAdministrator_Hides_Approved_Requests_Once_Started()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveType = SeedLeaveType(context, companyId);
        var today = DateOnly.FromDateTime(Now.UtcDateTime);

        var startedYesterday = CreateRequest(
            companyId, employeeId, leaveType.Id, Now, startDate: today.AddDays(-1), endDate: today.AddDays(1));
        startedYesterday.Approve(Guid.NewGuid(), Now);
        var startsToday = CreateRequest(
            companyId, employeeId, leaveType.Id, Now, startDate: today, endDate: today.AddDays(2));
        startsToday.Approve(Guid.NewGuid(), Now);
        context.LeaveRequests.AddRange(startedYesterday, startsToday);
        await context.SaveChangesAsync();

        var handler = new GetRecentLeaveRequestsHandler(
            context, new FakeEmployeeNameReader(), new FakeDirectReportsReader(), new FakeOpenTaskBySourceEntityReader(), new FakeClock(Now.UtcDateTime));

        var result = await handler.HandleAsync(
            new GetRecentLeaveRequestsRequest(companyId, null),
            Guid.NewGuid(),
            isHrAdministrator: true,
            CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_HrAdministrator_Still_Sees_NonApproved_Requests_That_Have_Started()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveType = SeedLeaveType(context, companyId);
        var today = DateOnly.FromDateTime(Now.UtcDateTime);

        // Only Status == Approved is affected by the "hide once started" rule — a pending
        // request covering a date range that has already begun (e.g. submitted late) must
        // still surface for an admin to action.
        var pending = CreateRequest(
            companyId, employeeId, leaveType.Id, Now, startDate: today.AddDays(-1), endDate: today.AddDays(1));
        context.LeaveRequests.Add(pending);
        await context.SaveChangesAsync();

        var handler = new GetRecentLeaveRequestsHandler(
            context, new FakeEmployeeNameReader(), new FakeDirectReportsReader(), new FakeOpenTaskBySourceEntityReader(), new FakeClock(Now.UtcDateTime));

        var result = await handler.HandleAsync(
            new GetRecentLeaveRequestsRequest(companyId, null),
            Guid.NewGuid(),
            isHrAdministrator: true,
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(pending.Id, item.LeaveRequestId);
    }

    // ── TaskId ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_TaskId_Is_Null_When_No_Open_Task_Exists()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveType = SeedLeaveType(context, companyId);
        var request = CreateRequest(companyId, employeeId, leaveType.Id, Now);
        context.LeaveRequests.Add(request);
        await context.SaveChangesAsync();

        var handler = new GetRecentLeaveRequestsHandler(
            context, new FakeEmployeeNameReader(), new FakeDirectReportsReader(), new FakeOpenTaskBySourceEntityReader(), new FakeClock(Now.UtcDateTime));

        var result = await handler.HandleAsync(
            new GetRecentLeaveRequestsRequest(companyId, null),
            Guid.NewGuid(),
            isHrAdministrator: true,
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Null(item.TaskId);
    }

    [Fact]
    public async Task HandleAsync_TaskId_Is_Populated_When_Open_Task_Exists()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveType = SeedLeaveType(context, companyId);
        var request = CreateRequest(companyId, employeeId, leaveType.Id, Now);
        context.LeaveRequests.Add(request);
        await context.SaveChangesAsync();

        var taskId = Guid.NewGuid();
        var openTaskReader = new FakeOpenTaskBySourceEntityReader(new Dictionary<Guid, Guid> { [request.Id] = taskId });
        var handler = new GetRecentLeaveRequestsHandler(
            context, new FakeEmployeeNameReader(), new FakeDirectReportsReader(), openTaskReader, new FakeClock(Now.UtcDateTime));

        var result = await handler.HandleAsync(
            new GetRecentLeaveRequestsRequest(companyId, null),
            Guid.NewGuid(),
            isHrAdministrator: true,
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(taskId, item.TaskId);
    }

    private static LeaveType SeedLeaveType(LeaveDbContext context, Guid companyId)
    {
        var leaveType = LeaveType.Create(
            Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, Now);
        context.LeaveTypes.Add(leaveType);
        return leaveType;
    }

    private static LeaveRequest CreateRequest(
        Guid companyId, Guid employeeId, Guid leaveTypeId, DateTimeOffset createdAt,
        DateOnly? startDate = null, DateOnly? endDate = null) =>
        LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(),
            startDate ?? new DateOnly(2026, 7, 1), LeaveDayPart.FullDay,
            endDate ?? new DateOnly(2026, 7, 3), LeaveDayPart.FullDay,
            3m, "Trip", createdAt);

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LeaveDbContext(options);
    }
}
