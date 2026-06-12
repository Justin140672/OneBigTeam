using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.RejectLeaveRequest;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class RejectLeaveRequestHandlerTests
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

    private static RejectLeaveRequestRequest RejectRequest(
        Guid companyId, Guid employeeId, Guid leaveRequestId, Guid reviewerId, string? reason = null) =>
        new()
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            LeaveRequestId = leaveRequestId,
            ReviewedByEmployeeId = reviewerId,
            RejectionReason = reason
        };

    [Fact]
    public async Task HandleAsync_Rejects_Pending_Request_And_Returns_Response()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveRequest = CreatePendingRequest(companyId, employeeId, now);
        context.LeaveRequests.Add(leaveRequest);
        await context.SaveChangesAsync();

        var rejectionTime = new DateTime(2026, 6, 13, 10, 0, 0, DateTimeKind.Utc);
        var handler = new RejectLeaveRequestHandler(context, new FakeClock(rejectionTime));
        var result = await handler.HandleAsync(
            RejectRequest(companyId, employeeId, leaveRequest.Id, reviewerId, "Team at capacity"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Rejected", result.Value!.Status);
        Assert.Equal(reviewerId, result.Value.ReviewedByEmployeeId);
        Assert.Equal(new DateTimeOffset(rejectionTime, TimeSpan.Zero), result.Value.ReviewedAt);
        Assert.Equal("Team at capacity", result.Value.RejectionReason);
        Assert.Equal(new DateTimeOffset(rejectionTime, TimeSpan.Zero), result.Value.UpdatedAt);

        var saved = await context.LeaveRequests.SingleAsync();
        Assert.Equal(LeaveRequestStatus.Rejected, saved.Status);
        Assert.Equal("Team at capacity", saved.RejectionReason);
    }

    [Fact]
    public async Task HandleAsync_Rejects_Without_Reason()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveRequest = CreatePendingRequest(companyId, employeeId, now);
        context.LeaveRequests.Add(leaveRequest);
        await context.SaveChangesAsync();

        var handler = new RejectLeaveRequestHandler(context, new FakeClock(FixedUtcNow));
        var result = await handler.HandleAsync(
            RejectRequest(companyId, employeeId, leaveRequest.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Rejected", result.Value!.Status);
        Assert.Null(result.Value.RejectionReason);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Request_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new RejectLeaveRequestHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            RejectRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
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

        var handler = new RejectLeaveRequestHandler(context, new FakeClock(FixedUtcNow));
        var result = await handler.HandleAsync(
            RejectRequest(companyId, Guid.NewGuid(), leaveRequest.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Rejects_Approved_Request()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveRequest = CreatePendingRequest(companyId, employeeId, now);
        leaveRequest.Approve(reviewerId, now);
        context.LeaveRequests.Add(leaveRequest);
        await context.SaveChangesAsync();

        var handler = new RejectLeaveRequestHandler(context, new FakeClock(FixedUtcNow));
        var result = await handler.HandleAsync(
            RejectRequest(companyId, employeeId, leaveRequest.Id, reviewerId, "Approved in error"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Rejected", result.Value!.Status);
        Assert.Equal("Approved in error", result.Value.RejectionReason);
    }

    [Fact]
    public async Task HandleAsync_Restores_Balance_When_Rejecting_Approved_Request()
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

        var handler = new RejectLeaveRequestHandler(context, new FakeClock(FixedUtcNow));
        var result = await handler.HandleAsync(
            RejectRequest(companyId, employeeId, leaveRequest.Id, Guid.NewGuid(), "Approved in error"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var savedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(0m, savedBalance.UsedDays);
        Assert.Equal(25m, savedBalance.RemainingDays);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Restore_Balance_When_Rejecting_Pending_Request()
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

        var handler = new RejectLeaveRequestHandler(context, new FakeClock(FixedUtcNow));
        var result = await handler.HandleAsync(
            RejectRequest(companyId, employeeId, leaveRequest.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var savedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(0m, savedBalance.UsedDays);
        Assert.Equal(25m, savedBalance.RemainingDays);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Already_Rejected()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveRequest = CreatePendingRequest(companyId, employeeId, now);
        leaveRequest.Reject(reviewerId, now);
        context.LeaveRequests.Add(leaveRequest);
        await context.SaveChangesAsync();

        var handler = new RejectLeaveRequestHandler(context, new FakeClock(FixedUtcNow));
        var result = await handler.HandleAsync(
            RejectRequest(companyId, employeeId, leaveRequest.Id, reviewerId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Cancelled()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveRequest = CreatePendingRequest(companyId, employeeId, now);
        leaveRequest.Cancel(now);
        context.LeaveRequests.Add(leaveRequest);
        await context.SaveChangesAsync();

        var handler = new RejectLeaveRequestHandler(context, new FakeClock(FixedUtcNow));
        var result = await handler.HandleAsync(
            RejectRequest(companyId, employeeId, leaveRequest.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_When_Rejecting_Approved_Request_With_No_Balance_Record()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveRequest = CreatePendingRequest(companyId, employeeId, now);
        leaveRequest.Approve(Guid.NewGuid(), now);
        context.LeaveRequests.Add(leaveRequest);
        await context.SaveChangesAsync();

        var handler = new RejectLeaveRequestHandler(context, new FakeClock(FixedUtcNow));
        var result = await handler.HandleAsync(
            RejectRequest(companyId, employeeId, leaveRequest.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Rejected", result.Value!.Status);
    }
}
