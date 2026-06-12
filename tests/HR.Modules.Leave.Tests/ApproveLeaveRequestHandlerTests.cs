using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.ApproveLeaveRequest;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class ApproveLeaveRequestHandlerTests
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

    private static ApproveLeaveRequestRequest ApproveRequest(Guid companyId, Guid employeeId, Guid leaveRequestId, Guid reviewerId) =>
        new()
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            LeaveRequestId = leaveRequestId,
            ReviewedByEmployeeId = reviewerId
        };

    [Fact]
    public async Task HandleAsync_Approves_Pending_Request_And_Returns_Response()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveRequest = CreatePendingRequest(companyId, employeeId, now);
        context.LeaveRequests.Add(leaveRequest);
        await context.SaveChangesAsync();

        var approvalTime = new DateTime(2026, 6, 13, 10, 0, 0, DateTimeKind.Utc);
        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(approvalTime));
        var result = await handler.HandleAsync(
            ApproveRequest(companyId, employeeId, leaveRequest.Id, reviewerId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Approved", result.Value!.Status);
        Assert.Equal(reviewerId, result.Value.ReviewedByEmployeeId);
        Assert.Equal(new DateTimeOffset(approvalTime, TimeSpan.Zero), result.Value.ReviewedAt);
        Assert.Equal(new DateTimeOffset(approvalTime, TimeSpan.Zero), result.Value.UpdatedAt);

        var saved = await context.LeaveRequests.SingleAsync();
        Assert.Equal(LeaveRequestStatus.Approved, saved.Status);
        Assert.Equal(reviewerId, saved.ReviewedByEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Request_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow));

        var result = await handler.HandleAsync(
            ApproveRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
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

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow));
        var result = await handler.HandleAsync(
            ApproveRequest(companyId, Guid.NewGuid(), leaveRequest.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Already_Approved()
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

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow));
        var result = await handler.HandleAsync(
            ApproveRequest(companyId, employeeId, leaveRequest.Id, reviewerId),
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

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow));
        var result = await handler.HandleAsync(
            ApproveRequest(companyId, employeeId, leaveRequest.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Rejected()
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

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow));
        var result = await handler.HandleAsync(
            ApproveRequest(companyId, employeeId, leaveRequest.Id, reviewerId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }
}
