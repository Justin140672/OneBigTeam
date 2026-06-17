using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.CancelLeaveRequest;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class CancelLeaveRequestHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 12, 9, 0, 0, DateTimeKind.Utc);

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LeaveDbContext(options);
    }

    private static LeaveRequest CreatePendingRequest(Guid companyId, Guid employeeId, DateTimeOffset now) =>
        LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 7), LeaveDayPart.FullDay,
            5m, "Holiday", now);

    [Fact]
    public async Task HandleAsync_Cancels_Pending_Request_And_Returns_Response()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveRequest = CreatePendingRequest(companyId, employeeId, now);
        context.LeaveRequests.Add(leaveRequest);
        await context.SaveChangesAsync();

        var cancelTime = new DateTime(2026, 6, 13, 10, 0, 0, DateTimeKind.Utc);
        var handler = new CancelLeaveRequestHandler(context, new FakeClock(cancelTime), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
        var result = await handler.HandleAsync(
            new CancelLeaveRequestRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                LeaveRequestId = leaveRequest.Id
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Cancelled", result.Value!.Status);
        Assert.Equal(new DateTimeOffset(cancelTime, TimeSpan.Zero), result.Value.UpdatedAt);

        var saved = await context.LeaveRequests.SingleAsync();
        Assert.Equal(LeaveRequestStatus.Cancelled, saved.Status);
    }

    [Fact]
    public async Task HandleAsync_Cancels_Approved_Request()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveRequest = CreatePendingRequest(companyId, employeeId, now);
        leaveRequest.Approve(Guid.NewGuid(), now);
        context.LeaveRequests.Add(leaveRequest);
        await context.SaveChangesAsync();

        var handler = new CancelLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
        var result = await handler.HandleAsync(
            new CancelLeaveRequestRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                LeaveRequestId = leaveRequest.Id
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Cancelled", result.Value!.Status);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Request_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new CancelLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());

        var result = await handler.HandleAsync(
            new CancelLeaveRequestRequest
            {
                CompanyId = Guid.NewGuid(),
                EmployeeId = Guid.NewGuid(),
                LeaveRequestId = Guid.NewGuid()
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Request_Belongs_To_Different_Employee()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveRequest = CreatePendingRequest(companyId, Guid.NewGuid(), now);
        context.LeaveRequests.Add(leaveRequest);
        await context.SaveChangesAsync();

        var handler = new CancelLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
        var result = await handler.HandleAsync(
            new CancelLeaveRequestRequest
            {
                CompanyId = companyId,
                EmployeeId = Guid.NewGuid(),
                LeaveRequestId = leaveRequest.Id
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Request_Belongs_To_Different_Company()
    {
        await using var context = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveRequest = CreatePendingRequest(companyA, employeeId, now);
        context.LeaveRequests.Add(leaveRequest);
        await context.SaveChangesAsync();

        var handler = new CancelLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
        var result = await handler.HandleAsync(
            new CancelLeaveRequestRequest
            {
                CompanyId = companyB,
                EmployeeId = employeeId,
                LeaveRequestId = leaveRequest.Id
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Request_Already_Cancelled()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveRequest = CreatePendingRequest(companyId, employeeId, now);
        leaveRequest.Cancel(now);
        context.LeaveRequests.Add(leaveRequest);
        await context.SaveChangesAsync();

        var handler = new CancelLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
        var result = await handler.HandleAsync(
            new CancelLeaveRequestRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                LeaveRequestId = leaveRequest.Id
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Request_Is_Rejected()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveRequest = CreatePendingRequest(companyId, employeeId, now);
        leaveRequest.Reject(Guid.NewGuid(), now);
        context.LeaveRequests.Add(leaveRequest);
        await context.SaveChangesAsync();

        var handler = new CancelLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
        var result = await handler.HandleAsync(
            new CancelLeaveRequestRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                LeaveRequestId = leaveRequest.Id
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Restores_Balance_When_Cancelling_Approved_Request()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveRequest = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(),
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 7), LeaveDayPart.FullDay,
            5m, "Holiday", now);
        leaveRequest.Approve(Guid.NewGuid(), now);

        var balance = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(),
            2026, 25m, now);
        balance.RecordUsage(5m, now);

        context.LeaveRequests.Add(leaveRequest);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new CancelLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
        var result = await handler.HandleAsync(
            new CancelLeaveRequestRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                LeaveRequestId = leaveRequest.Id
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var savedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(0m, savedBalance.UsedDays);
        Assert.Equal(25m, savedBalance.RemainingDays);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Restore_Balance_When_Cancelling_Pending_Request()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveRequest = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(),
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 7), LeaveDayPart.FullDay,
            5m, "Holiday", now);

        var balance = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(),
            2026, 25m, now);

        context.LeaveRequests.Add(leaveRequest);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new CancelLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
        var result = await handler.HandleAsync(
            new CancelLeaveRequestRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                LeaveRequestId = leaveRequest.Id
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var savedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(0m, savedBalance.UsedDays);
        Assert.Equal(25m, savedBalance.RemainingDays);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_When_Cancelling_Approved_Request_With_No_Balance_Record()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveRequest = CreatePendingRequest(companyId, employeeId, now);
        leaveRequest.Approve(Guid.NewGuid(), now);
        context.LeaveRequests.Add(leaveRequest);
        await context.SaveChangesAsync();

        var handler = new CancelLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
        var result = await handler.HandleAsync(
            new CancelLeaveRequestRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                LeaveRequestId = leaveRequest.Id
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Cancelled", result.Value!.Status);
    }

    [Fact]
    public async Task HandleAsync_Restores_Toil_Balance_When_Cancelling_Approved_Toil_Request()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "TOIL", "TOIL", 0,
            AccrualMethod.None, LeaveTypeBehaviour.Toil, now);

        var leaveRequest = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(),
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 5), LeaveDayPart.FullDay,
            3m, null, now);
        leaveRequest.Approve(Guid.NewGuid(), now);

        var balance = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(), 2026, 0m, now);
        balance.Adjust(5m, now);
        balance.RecordUsage(3m, now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveRequests.Add(leaveRequest);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new CancelLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
        var result = await handler.HandleAsync(
            new CancelLeaveRequestRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                LeaveRequestId = leaveRequest.Id
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var savedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(0m, savedBalance.UsedDays);
        Assert.Equal(5m, savedBalance.RemainingDays); // fully restored
    }

    [Fact]
    public async Task HandleAsync_Restores_Toil_Balance_From_Different_Policy_Year()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "TOIL", "TOIL", 0,
            AccrualMethod.None, LeaveTypeBehaviour.Toil, now);

        // TOIL earned in 2025, leave taken in 2026
        var leaveRequest = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(),
            new DateOnly(2026, 1, 5), LeaveDayPart.FullDay,
            new DateOnly(2026, 1, 7), LeaveDayPart.FullDay,
            3m, null, now);
        leaveRequest.Approve(Guid.NewGuid(), now);

        var balance2025 = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(), 2025, 0m, now);
        balance2025.Adjust(4m, now);
        balance2025.RecordUsage(3m, now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveRequests.Add(leaveRequest);
        context.LeaveBalances.Add(balance2025);
        await context.SaveChangesAsync();

        var handler = new CancelLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
        var result = await handler.HandleAsync(
            new CancelLeaveRequestRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                LeaveRequestId = leaveRequest.Id
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var savedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(0m, savedBalance.UsedDays);
        Assert.Equal(4m, savedBalance.RemainingDays); // 2025 balance fully restored
    }

    [Fact]
    public async Task HandleAsync_Publishes_LeaveCancelledAuditEvent()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveRequest = CreatePendingRequest(companyId, employeeId, now);
        context.LeaveRequests.Add(leaveRequest);
        await context.SaveChangesAsync();

        var auditPublisher = new CapturingAuditEventPublisher();
        var cancelTime = new DateTime(2026, 6, 13, 10, 0, 0, DateTimeKind.Utc);
        var handler = new CancelLeaveRequestHandler(context, new FakeClock(cancelTime), new FakeCompanyLeaveSettingsReader(), auditPublisher);
        await handler.HandleAsync(
            new CancelLeaveRequestRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                LeaveRequestId = leaveRequest.Id
            },
            CancellationToken.None);

        var auditEvt = Assert.Single(auditPublisher.Published);
        var auditEvent = Assert.IsType<LeaveCancelledAuditEvent>(auditEvt);
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(employeeId, auditEvent.EmployeeId);
        Assert.Equal(leaveRequest.Id, auditEvent.LeaveRequestId);
        Assert.Equal(leaveRequest.LeaveTypeId, auditEvent.LeaveTypeId);
        Assert.Equal(new DateOnly(2026, 8, 3), auditEvent.StartDate);
        Assert.Equal(new DateOnly(2026, 8, 7), auditEvent.EndDate);
        Assert.Equal(5m, auditEvent.TotalDays);
        Assert.Equal("Pending", auditEvent.PreviousStatus);
        Assert.Equal(new DateTimeOffset(cancelTime, TimeSpan.Zero), auditEvent.OccurredAt);
    }
}
