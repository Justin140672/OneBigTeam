using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Services;
using HR.Modules.Leave.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class LeaveApprovalEffectsServiceTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 12, 9, 0, 0, DateTimeKind.Utc);

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LeaveDbContext(options);
    }

    private static LeaveApprovalEffectsService BuildService(LeaveDbContext context) =>
        new(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(),
            new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context));

    [Fact]
    public async Task ApplyBalanceEffectsAndApproveAsync_Consumes_Toil_Ledger()
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
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(), 2026, 0m, new DateOnly(2026, 1, 1), now);
        balance.Adjust(5m, now);
        var bucket = ToilTransaction.CreateEarned(
            Guid.NewGuid(), companyId, employeeId, balance.Id, Guid.NewGuid(), 5m, new DateOnly(2026, 5, 1), null, null, now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveRequests.Add(leaveRequest);
        context.LeaveBalances.Add(balance);
        context.ToilTransactions.Add(bucket);
        await context.SaveChangesAsync();

        var service = BuildService(context);
        var result = await service.ApplyBalanceEffectsAndApproveAsync(leaveRequest, leaveType, Guid.NewGuid(), now, CancellationToken.None);
        await context.SaveChangesAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(LeaveRequestStatus.Approved, leaveRequest.Status);

        var savedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(3m, savedBalance.UsedDays);
    }

    [Fact]
    public async Task ApplyBalanceEffectsAndApproveAsync_Fails_When_No_LeaveBalance_Row_For_Policy_Year()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);

        var leaveRequest = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(),
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 7), LeaveDayPart.FullDay,
            5m, "Holiday", now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveRequests.Add(leaveRequest);
        await context.SaveChangesAsync();

        var service = BuildService(context);
        var result = await service.ApplyBalanceEffectsAndApproveAsync(leaveRequest, leaveType, Guid.NewGuid(), now, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Equal(LeaveRequestStatus.Pending, leaveRequest.Status);
    }

    [Fact]
    public async Task ApplyBalanceEffectsAndApproveAsync_Succeeds_Without_Balance_Row_When_HasBalance_Is_False()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Unpaid Leave", "UNPAID", 0,
            AccrualMethod.None, LeaveTypeBehaviour.Standard, now, hasBalance: false);

        var leaveRequest = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(),
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 7), LeaveDayPart.FullDay,
            5m, "Unpaid", now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveRequests.Add(leaveRequest);
        await context.SaveChangesAsync();

        var service = BuildService(context);
        var result = await service.ApplyBalanceEffectsAndApproveAsync(leaveRequest, leaveType, Guid.NewGuid(), now, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(LeaveRequestStatus.Approved, leaveRequest.Status);
        Assert.Empty(context.LeaveBalances);
    }

    [Fact]
    public async Task PublishApprovalOutcomeAsync_Publishes_Notification_AuditEvent_And_IntegrationEvent()
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
        leaveRequest.Approve(reviewerId, now);

        var notif = new FakeNotificationWriter();
        var auditPublisher = new CapturingAuditEventPublisher();
        var integrationPublisher = new CapturingIntegrationEventPublisher();
        var service = new LeaveApprovalEffectsService(
            context, notif, integrationPublisher, new FakeCompanyLeaveSettingsReader(),
            auditPublisher, new ToilLedgerService(context));

        await service.PublishApprovalOutcomeAsync(leaveRequest, reviewerId, now, CancellationToken.None);

        Assert.Single(notif.Written);
        Assert.IsType<LeaveApprovedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.IsType<LeaveApprovedIntegrationEvent>(Assert.Single(integrationPublisher.Published));
    }
}
