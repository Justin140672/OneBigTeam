using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.ApproveLeaveRequest;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using HR.SharedKernel;
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
        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(approvalTime), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader());
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
        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader());

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

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader());
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

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader());
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

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader());
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

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader());
        var result = await handler.HandleAsync(
            ApproveRequest(companyId, employeeId, leaveRequest.Id, reviewerId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Deducts_Balance_On_Approval()
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

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader());
        var result = await handler.HandleAsync(
            ApproveRequest(companyId, employeeId, leaveRequest.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var savedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(5m, savedBalance.UsedDays);
        Assert.Equal(20m, savedBalance.RemainingDays);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_When_No_Balance_Record_Exists()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveRequest = CreatePendingRequest(companyId, employeeId, now);
        context.LeaveRequests.Add(leaveRequest);
        await context.SaveChangesAsync();

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader());
        var result = await handler.HandleAsync(
            ApproveRequest(companyId, employeeId, leaveRequest.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Approved", result.Value!.Status);
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

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader());
        var result = await handler.HandleAsync(
            ApproveRequest(companyB, employeeId, leaveRequest.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Deducts_Toil_Balance_On_Approval()
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

        var balance = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(), 2026, 0m, now);
        balance.Adjust(5m, now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveRequests.Add(leaveRequest);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader());
        var result = await handler.HandleAsync(
            ApproveRequest(companyId, employeeId, leaveRequest.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var savedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(3m, savedBalance.UsedDays);
        Assert.Equal(2m, savedBalance.RemainingDays); // 0 entitlement + 5 adjustment - 3 used
    }

    [Fact]
    public async Task HandleAsync_Deducts_Toil_Balance_From_Different_Policy_Year()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "TOIL", "TOIL", 0,
            AccrualMethod.None, LeaveTypeBehaviour.Toil, now);

        // TOIL earned in 2025, leave taken in 2026 — different policy years
        var leaveRequest = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(),
            new DateOnly(2026, 1, 5), LeaveDayPart.FullDay,
            new DateOnly(2026, 1, 7), LeaveDayPart.FullDay,
            3m, null, now);

        var balance2025 = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(), 2025, 0m, now);
        balance2025.Adjust(4m, now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveRequests.Add(leaveRequest);
        context.LeaveBalances.Add(balance2025);
        await context.SaveChangesAsync();

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader());
        var result = await handler.HandleAsync(
            ApproveRequest(companyId, employeeId, leaveRequest.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var savedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(3m, savedBalance.UsedDays);
        Assert.Equal(1m, savedBalance.RemainingDays); // deducted from 2025 balance despite 2026 leave dates
    }

    [Fact]
    public async Task HandleAsync_Deducts_Toil_From_Oldest_Balance_First()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "TOIL", "TOIL", 0,
            AccrualMethod.None, LeaveTypeBehaviour.Toil, now);

        var leaveRequest = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(),
            new DateOnly(2026, 6, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 6, 3), LeaveDayPart.FullDay,
            1m, null, now);

        var balance2025 = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(), 2025, 0m, now);
        balance2025.Adjust(2m, now);

        var balance2026 = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(), 2026, 0m, now);
        balance2026.Adjust(3m, now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveRequests.Add(leaveRequest);
        context.LeaveBalances.AddRange(balance2025, balance2026);
        await context.SaveChangesAsync();

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader());
        var result = await handler.HandleAsync(
            ApproveRequest(companyId, employeeId, leaveRequest.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved2025 = await context.LeaveBalances.SingleAsync(b => b.PolicyYear == 2025);
        var saved2026 = await context.LeaveBalances.SingleAsync(b => b.PolicyYear == 2026);
        Assert.Equal(1m, saved2025.UsedDays);  // deducted from the older balance
        Assert.Equal(0m, saved2026.UsedDays);  // 2026 balance untouched
    }

    [Fact]
    public async Task HandleAsync_Publishes_LeaveApprovedIntegrationEvent()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveRequest = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 7), LeaveDayPart.FullDay,
            5m, "Holiday", now);
        context.LeaveRequests.Add(leaveRequest);
        await context.SaveChangesAsync();

        var publisher = new CapturingIntegrationEventPublisher();
        var approvalTime = new DateTime(2026, 6, 13, 10, 0, 0, DateTimeKind.Utc);
        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(approvalTime), publisher, new FakeCompanyLeaveSettingsReader());
        await handler.HandleAsync(
            ApproveRequest(companyId, employeeId, leaveRequest.Id, reviewerId),
            CancellationToken.None);

        var evt = Assert.Single(publisher.Published);
        var approved = Assert.IsType<LeaveApprovedIntegrationEvent>(evt);
        Assert.Equal(companyId, approved.CompanyId);
        Assert.Equal(employeeId, approved.EmployeeId);
        Assert.Equal(leaveRequest.Id, approved.LeaveRequestId);
        Assert.Equal(leaveRequest.LeaveTypeId, approved.LeaveTypeId);
        Assert.Equal(new DateOnly(2026, 8, 3), approved.StartDate);
        Assert.Equal(new DateOnly(2026, 8, 7), approved.EndDate);
        Assert.Equal(5m, approved.TotalDays);
        Assert.Equal(reviewerId, approved.ReviewedByEmployeeId);
        Assert.Equal(new DateTimeOffset(approvalTime, TimeSpan.Zero), approved.OccurredAt);
    }
}
