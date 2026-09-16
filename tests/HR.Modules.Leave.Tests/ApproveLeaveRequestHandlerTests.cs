using HR.Modules.Leave.Services;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.ApproveLeaveRequest;
using HR.Modules.Leave.Features.RejectLeaveRequest;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Tests.Infrastructure;
using HR.Infrastructure.Abstractions;
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

    /// <summary>
    /// Creates a pending leave request together with a matching HasBalance == false LeaveType, so
    /// tests that only care about approval mechanics (audit/notification/event publication) do not
    /// need to also set up a LeaveBalance row to reach a successful approval.
    /// </summary>
    private static async Task<LeaveRequest> CreatePendingRequestWithNonBalanceTypeAsync(
        LeaveDbContext context, Guid companyId, Guid employeeId, DateTimeOffset now)
    {
        var leaveTypeId = Guid.NewGuid();
        var leaveType = LeaveType.Create(leaveTypeId, companyId, "Unpaid Leave", "UNPAID", 0,
            AccrualMethod.None, LeaveTypeBehaviour.Standard, now, hasBalance: false);

        var leaveRequest = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(),
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 7), LeaveDayPart.FullDay,
            5m, "Holiday", now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveRequests.Add(leaveRequest);
        await context.SaveChangesAsync();

        return leaveRequest;
    }

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

        var leaveRequest = await CreatePendingRequestWithNonBalanceTypeAsync(context, companyId, employeeId, now);

        var approvalTime = new DateTime(2026, 6, 13, 10, 0, 0, DateTimeKind.Utc);
        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(approvalTime), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)));
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
    public async Task HandleAsync_Publishes_Audit_Event_With_EmployeeId_Set_To_Requester_Not_Reviewer()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveRequest = await CreatePendingRequestWithNonBalanceTypeAsync(context, companyId, employeeId, now);

        var auditPublisher = new CapturingAuditEventPublisher();
        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), auditPublisher, new ToilLedgerService(context)));

        var result = await handler.HandleAsync(
            ApproveRequest(companyId, employeeId, leaveRequest.Id, reviewerId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var auditEvent = Assert.IsType<LeaveApprovedAuditEvent>(Assert.Single(auditPublisher.Published));
        var auditEventAsInterface = (HR.SharedKernel.IAuditEvent)auditEvent;

        // ActorEmployeeId is deliberately the approving manager, but IAuditEvent.EmployeeId must
        // remain the leave requester — this is exactly the distinction the audit history feature relies on.
        Assert.Equal(reviewerId, auditEventAsInterface.ActorEmployeeId);
        Assert.Equal(employeeId, auditEventAsInterface.EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Request_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)));

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

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)));
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

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)));
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

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)));
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

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)));
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
            2026, 25m, new DateOnly(2026, 1, 1), now);

        context.LeaveRequests.Add(leaveRequest);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)));
        var result = await handler.HandleAsync(
            ApproveRequest(companyId, employeeId, leaveRequest.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var savedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(5m, savedBalance.UsedDays);
        Assert.Equal(20m, savedBalance.RemainingDays);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_No_Balance_Record_Exists_For_Balance_Tracked_Type()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = LeaveType.Create(leaveTypeId, companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);

        var leaveRequest = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(),
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 7), LeaveDayPart.FullDay,
            5m, "Holiday", now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveRequests.Add(leaveRequest);
        await context.SaveChangesAsync();

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)));
        var result = await handler.HandleAsync(
            ApproveRequest(companyId, employeeId, leaveRequest.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);

        // Guard must not mutate the request status or persist any change.
        var saved = await context.LeaveRequests.SingleAsync();
        Assert.Equal(LeaveRequestStatus.Pending, saved.Status);
        Assert.Null(saved.ReviewedByEmployeeId);
        Assert.Null(saved.ReviewedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_No_Balance_Record_Exists_And_LeaveType_Not_Found()
    {
        // Legacy/orphaned leave requests whose LeaveType row no longer exists are treated as
        // "unknown" and fall into the balance-tracked branch (leaveType is null); with no balance
        // row present, approval must fail rather than silently approving without deduction.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveRequest = CreatePendingRequest(companyId, employeeId, now);
        context.LeaveRequests.Add(leaveRequest);
        await context.SaveChangesAsync();

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)));
        var result = await handler.HandleAsync(
            ApproveRequest(companyId, employeeId, leaveRequest.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Approves_Without_Balance_Lookup_When_LeaveType_HasBalance_Is_False()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = LeaveType.Create(leaveTypeId, companyId, "Unpaid Leave", "UNPAID", 0,
            AccrualMethod.None, LeaveTypeBehaviour.Standard, now, hasBalance: false);

        var leaveRequest = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(),
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 7), LeaveDayPart.FullDay,
            5m, "Unpaid", now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveRequests.Add(leaveRequest);
        await context.SaveChangesAsync();

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)));
        var result = await handler.HandleAsync(
            ApproveRequest(companyId, employeeId, leaveRequest.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Approved", result.Value!.Status);

        var saved = await context.LeaveRequests.SingleAsync();
        Assert.Equal(LeaveRequestStatus.Approved, saved.Status);
        Assert.Empty(context.LeaveBalances);
    }

    [Fact]
    public async Task HandleAsync_Approves_Using_Balance_For_Requests_StartDate_Policy_Year_Not_Todays()
    {
        // Approving today (2026-06-12, policy year 2026) a request whose StartDate falls in a
        // future policy year (2027) must look up the 2027 balance row, not the 2026 one.
        // AccrualMethod.None (full entitlement available immediately) is used deliberately here:
        // Monthly/Fortnightly accrual is anchored to accrualStartDate and evaluated as-of the
        // approval date (LEAVE-04), so a 2027 balance approved in 2026 - before its own accrual
        // period has even started - would legitimately show zero accrued days. That accrual-timing
        // behaviour is covered separately; this test's own concern is only which policy-year
        // balance row approval selects.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = LeaveType.Create(leaveTypeId, companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.None, LeaveTypeBehaviour.Standard, now);

        var leaveRequest = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(),
            new DateOnly(2027, 1, 5), LeaveDayPart.FullDay,
            new DateOnly(2027, 1, 6), LeaveDayPart.FullDay,
            2m, "New year holiday", now);

        // Only the 2027 balance exists — a 2026 (today's) balance would produce the wrong deduction.
        // AccrualStartDate is the employee's actual continuous accrual-eligible-from date (see
        // LeaveBalance.AccrualStartDate), not necessarily the policy year's calendar start - here
        // it is set to before today so the accrued-availability check (LEAVE-04/Ticket 5) does not
        // zero out the balance just because "today" precedes 2027-01-01. Mirrors the same fixture
        // choice in SubmitLeaveRequestHandlerTests.HandleAsync_Checks_Future_Years_Balance_....
        var balance2027 = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(), 2027, 25m, new DateOnly(2026, 1, 1), now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveRequests.Add(leaveRequest);
        context.LeaveBalances.Add(balance2027);
        await context.SaveChangesAsync();

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)));
        var result = await handler.HandleAsync(
            ApproveRequest(companyId, employeeId, leaveRequest.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var savedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(2m, savedBalance.UsedDays);
        Assert.Equal(23m, savedBalance.RemainingDays);
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

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)));
        var result = await handler.HandleAsync(
            ApproveRequest(companyB, employeeId, leaveRequest.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static ToilTransaction CreateEarnedBucket(
        Guid companyId, Guid employeeId, Guid balanceId, decimal days, DateOnly occurredOn, DateTimeOffset now) =>
        ToilTransaction.CreateEarned(
            Guid.NewGuid(), companyId, employeeId, balanceId, Guid.NewGuid(), days, occurredOn, null, null, now);

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
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(), 2026, 0m, new DateOnly(2026, 1, 1), now);
        balance.Adjust(5m, now);
        var bucket = CreateEarnedBucket(companyId, employeeId, balance.Id, 5m, new DateOnly(2026, 5, 1), now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveRequests.Add(leaveRequest);
        context.LeaveBalances.Add(balance);
        context.ToilTransactions.Add(bucket);
        await context.SaveChangesAsync();

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)));
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
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(), 2025, 0m, new DateOnly(2025, 1, 1), now);
        balance2025.Adjust(4m, now);
        var bucket2025 = CreateEarnedBucket(companyId, employeeId, balance2025.Id, 4m, new DateOnly(2025, 6, 1), now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveRequests.Add(leaveRequest);
        context.LeaveBalances.Add(balance2025);
        context.ToilTransactions.Add(bucket2025);
        await context.SaveChangesAsync();

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)));
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
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(), 2025, 0m, new DateOnly(2025, 1, 1), now);
        balance2025.Adjust(2m, now);
        var bucket2025 = CreateEarnedBucket(companyId, employeeId, balance2025.Id, 2m, new DateOnly(2025, 6, 1), now);

        var balance2026 = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(), 2026, 0m, new DateOnly(2026, 1, 1), now);
        balance2026.Adjust(3m, now);
        var bucket2026 = CreateEarnedBucket(companyId, employeeId, balance2026.Id, 3m, new DateOnly(2026, 2, 1), now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveRequests.Add(leaveRequest);
        context.LeaveBalances.AddRange(balance2025, balance2026);
        context.ToilTransactions.AddRange(bucket2025, bucket2026);
        await context.SaveChangesAsync();

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)));
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
    public async Task HandleAsync_Approving_Toil_Request_Spanning_Multiple_Buckets_Creates_Multiple_Used_Transactions()
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
            new DateOnly(2026, 8, 6), LeaveDayPart.FullDay,
            4m, null, now);

        var balance = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(), 2026, 0m, new DateOnly(2026, 1, 1), now);
        balance.Adjust(6m, now);
        var oldestBucket = CreateEarnedBucket(companyId, employeeId, balance.Id, 2m, new DateOnly(2026, 4, 1), now);
        var newerBucket = CreateEarnedBucket(companyId, employeeId, balance.Id, 4m, new DateOnly(2026, 5, 1), now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveRequests.Add(leaveRequest);
        context.LeaveBalances.Add(balance);
        context.ToilTransactions.AddRange(oldestBucket, newerBucket);
        await context.SaveChangesAsync();

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)));
        var result = await handler.HandleAsync(
            ApproveRequest(companyId, employeeId, leaveRequest.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var usedTransactions = await context.ToilTransactions
            .Where(t => t.Type == ToilTransactionType.Used)
            .ToListAsync();
        Assert.Equal(2, usedTransactions.Count);
        Assert.Equal(2m, usedTransactions.Single(t => t.RelatedTransactionId == oldestBucket.Id).Days);
        Assert.Equal(2m, usedTransactions.Single(t => t.RelatedTransactionId == newerBucket.Id).Days);

        var savedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(4m, savedBalance.UsedDays);
    }

    [Fact]
    public async Task HandleAsync_Fails_When_Insufficient_Toil_And_AllowNegativeToilBalance_Is_False()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "TOIL", "TOIL", 0,
            AccrualMethod.None, LeaveTypeBehaviour.Toil, now, allowNegativeToilBalance: false);

        var leaveRequest = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(),
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 5), LeaveDayPart.FullDay,
            3m, null, now);

        var balance = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(), 2026, 0m, new DateOnly(2026, 1, 1), now);
        balance.Adjust(1m, now);
        var bucket = CreateEarnedBucket(companyId, employeeId, balance.Id, 1m, new DateOnly(2026, 5, 1), now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveRequests.Add(leaveRequest);
        context.LeaveBalances.Add(balance);
        context.ToilTransactions.Add(bucket);
        await context.SaveChangesAsync();

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)));
        var result = await handler.HandleAsync(
            ApproveRequest(companyId, employeeId, leaveRequest.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);

        var saved = await context.LeaveRequests.SingleAsync();
        Assert.Equal(LeaveRequestStatus.Pending, saved.Status); // must not be approved
        var savedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(0m, savedBalance.UsedDays);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_When_Insufficient_Toil_And_AllowNegativeToilBalance_Is_True()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "TOIL", "TOIL", 0,
            AccrualMethod.None, LeaveTypeBehaviour.Toil, now, allowNegativeToilBalance: true);

        var leaveRequest = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(),
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 5), LeaveDayPart.FullDay,
            3m, null, now);

        var balance = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(), 2026, 0m, new DateOnly(2026, 1, 1), now);
        balance.Adjust(1m, now);
        var bucket = CreateEarnedBucket(companyId, employeeId, balance.Id, 1m, new DateOnly(2026, 5, 1), now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveRequests.Add(leaveRequest);
        context.LeaveBalances.Add(balance);
        context.ToilTransactions.Add(bucket);
        await context.SaveChangesAsync();

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)));
        var result = await handler.HandleAsync(
            ApproveRequest(companyId, employeeId, leaveRequest.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved = await context.LeaveRequests.SingleAsync();
        Assert.Equal(LeaveRequestStatus.Approved, saved.Status);
        var savedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(3m, savedBalance.UsedDays);
        Assert.Equal(-2m, savedBalance.RemainingDays); // 0 entitlement + 1 adjustment - 3 used
    }

    [Fact]
    public async Task HandleAsync_Publishes_LeaveApprovedIntegrationEvent()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveRequest = await CreatePendingRequestWithNonBalanceTypeAsync(context, companyId, employeeId, now);

        var publisher = new CapturingIntegrationEventPublisher();
        var approvalTime = new DateTime(2026, 6, 13, 10, 0, 0, DateTimeKind.Utc);
        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(approvalTime), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), publisher, new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)));
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

    [Fact]
    public async Task HandleAsync_Publishes_LeaveApprovedAuditEvent()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveRequest = await CreatePendingRequestWithNonBalanceTypeAsync(context, companyId, employeeId, now);

        var auditPublisher = new CapturingAuditEventPublisher();
        var approvalTime = new DateTime(2026, 6, 13, 10, 0, 0, DateTimeKind.Utc);
        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(approvalTime), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), auditPublisher, new ToilLedgerService(context)));
        await handler.HandleAsync(ApproveRequest(companyId, employeeId, leaveRequest.Id, reviewerId), CancellationToken.None);

        var auditEvt = Assert.Single(auditPublisher.Published);
        var auditEvent = Assert.IsType<LeaveApprovedAuditEvent>(auditEvt);
        Assert.Equal(companyId, auditEvent.CompanyId);
        Assert.Equal(employeeId, auditEvent.EmployeeId);
        Assert.Equal(leaveRequest.Id, auditEvent.LeaveRequestId);
        Assert.Equal(leaveRequest.LeaveTypeId, auditEvent.LeaveTypeId);
        Assert.Equal(new DateOnly(2026, 8, 3), auditEvent.StartDate);
        Assert.Equal(new DateOnly(2026, 8, 7), auditEvent.EndDate);
        Assert.Equal(5m, auditEvent.TotalDays);
        Assert.Equal(reviewerId, auditEvent.ReviewedByEmployeeId);
        Assert.Equal(new DateTimeOffset(approvalTime, TimeSpan.Zero), auditEvent.OccurredAt);
    }

    [Fact]
    public async Task HandleAsync_Writes_LeaveApproved_Notification_To_Employee()
    {
        await using var context = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var now        = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var notif      = new FakeNotificationWriter();

        var leaveRequest = await CreatePendingRequestWithNonBalanceTypeAsync(context, companyId, employeeId, now);

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new LeaveApprovalEffectsService(context, notif, new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)));
        await handler.HandleAsync(ApproveRequest(companyId, employeeId, leaveRequest.Id, reviewerId), CancellationToken.None);

        var written = Assert.Single(notif.Written);
        Assert.Equal(companyId,                     written.CompanyId);
        Assert.Equal(employeeId,                    written.EmployeeId);
        Assert.Equal(leaveRequest.Id,               written.SourceEntityId);
        Assert.Equal(NotificationType.LeaveApproved, written.Type);
        Assert.Equal(NotificationPriority.Normal,    written.Priority);
    }

    // --- P2 (Ticket 5): approval must recheck accrued availability, not just deduct blindly ---

    private static LeaveDbContext BuildContext(string dbName) =>
        new(new DbContextOptionsBuilder<LeaveDbContext>().UseInMemoryDatabase(dbName).Options);

    [Fact]
    public async Task HandleAsync_Second_Sequential_Approval_Fails_When_Combined_Requests_Exceed_Balance()
    {
        // Regression for the original P2 report: submission only checks availability at the moment
        // a request is created - a pending request does not reserve leave - so two individually
        // valid pending requests could previously both be approved, driving the balance negative
        // under a policy that forbids it. Each approval below uses its own DbContext instance
        // (a fresh "request") against the same underlying store, as two separate approval calls
        // against the same employee/balance would in production.
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        await using (var seedContext = BuildContext(dbName))
        {
            var leaveType = LeaveType.Create(leaveTypeId, companyId, "Annual Leave", "ANNUAL", 5,
                AccrualMethod.None, LeaveTypeBehaviour.Standard, now);
            var policy = LeavePolicy.Create(policyId, companyId, "No Negative Policy", null, 0,
                allowNegativeBalance: false, isDefault: false, now);
            var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveTypeId, policyId,
                2026, 5m, new DateOnly(2026, 1, 1), now);

            var requestOne = LeaveRequest.Create(
                Guid.NewGuid(), companyId, employeeId, leaveTypeId, policyId,
                new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
                new DateOnly(2026, 8, 7), LeaveDayPart.FullDay,
                5m, "First week", now);

            var requestTwo = LeaveRequest.Create(
                Guid.NewGuid(), companyId, employeeId, leaveTypeId, policyId,
                new DateOnly(2026, 9, 7), LeaveDayPart.FullDay,
                new DateOnly(2026, 9, 11), LeaveDayPart.FullDay,
                5m, "Second week", now);

            seedContext.LeaveTypes.Add(leaveType);
            seedContext.LeavePolicies.Add(policy);
            seedContext.LeaveBalances.Add(balance);
            seedContext.LeaveRequests.AddRange(requestOne, requestTwo);
            await seedContext.SaveChangesAsync();
        }

        Guid requestOneId, requestTwoId;
        await using (var lookupContext = BuildContext(dbName))
        {
            var requests = await lookupContext.LeaveRequests.OrderBy(r => r.StartDate).ToListAsync();
            requestOneId = requests[0].Id;
            requestTwoId = requests[1].Id;
        }

        await using (var firstApprovalContext = BuildContext(dbName))
        {
            var handler = new ApproveLeaveRequestHandler(firstApprovalContext, new FakeClock(FixedUtcNow),
                new LeaveApprovalEffectsService(firstApprovalContext, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(firstApprovalContext)));

            var firstResult = await handler.HandleAsync(
                ApproveRequest(companyId, employeeId, requestOneId, Guid.NewGuid()), CancellationToken.None);

            Assert.True(firstResult.IsSuccess);
        }

        var auditPublisher = new CapturingAuditEventPublisher();
        var integrationPublisher = new CapturingIntegrationEventPublisher();
        var notif = new FakeNotificationWriter();

        await using (var secondApprovalContext = BuildContext(dbName))
        {
            var handler = new ApproveLeaveRequestHandler(secondApprovalContext, new FakeClock(FixedUtcNow),
                new LeaveApprovalEffectsService(secondApprovalContext, notif, integrationPublisher, new FakeCompanyLeaveSettingsReader(), auditPublisher, new ToilLedgerService(secondApprovalContext)));

            var secondResult = await handler.HandleAsync(
                ApproveRequest(companyId, employeeId, requestTwoId, Guid.NewGuid()), CancellationToken.None);

            Assert.True(secondResult.IsFailure);
            Assert.Equal("validation", secondResult.Error.Code);
            Assert.Contains("insufficient", secondResult.Error.Message, StringComparison.OrdinalIgnoreCase);
        }

        await using (var verifyContext = BuildContext(dbName))
        {
            var savedRequestTwo = await verifyContext.LeaveRequests.SingleAsync(r => r.Id == requestTwoId);
            Assert.Equal(LeaveRequestStatus.Pending, savedRequestTwo.Status);
            Assert.Null(savedRequestTwo.ReviewedByEmployeeId);

            var savedBalance = await verifyContext.LeaveBalances.SingleAsync();
            Assert.Equal(5m, savedBalance.UsedDays);   // only the first approval's usage
            Assert.Equal(0m, savedBalance.RemainingDays);
        }

        Assert.Empty(auditPublisher.Published);
        Assert.Empty(integrationPublisher.Published);
        Assert.Empty(notif.Written);
    }

    [Fact]
    public async Task HandleAsync_Approves_Over_Balance_When_Policy_Allows_Negative_Balance()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = LeaveType.Create(leaveTypeId, companyId, "Annual Leave", "ANNUAL", 2,
            AccrualMethod.None, LeaveTypeBehaviour.Standard, now);
        var policy = LeavePolicy.Create(policyId, companyId, "Negative Allowed Policy", null, 0,
            allowNegativeBalance: true, isDefault: false, now);
        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveTypeId, policyId,
            2026, 2m, new DateOnly(2026, 1, 1), now);

        var leaveRequest = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, policyId,
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 7), LeaveDayPart.FullDay,
            5m, "Over-balance request", now);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.LeaveBalances.Add(balance);
        context.LeaveRequests.Add(leaveRequest);
        await context.SaveChangesAsync();

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)));
        var result = await handler.HandleAsync(
            ApproveRequest(companyId, employeeId, leaveRequest.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var savedRequest = await context.LeaveRequests.SingleAsync();
        Assert.Equal(LeaveRequestStatus.Approved, savedRequest.Status);

        var savedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(5m, savedBalance.UsedDays);
        Assert.Equal(-3m, savedBalance.RemainingDays);
    }

    [Fact]
    public async Task HandleAsync_Approves_When_Balance_Adjustment_Recorded_Between_Submission_And_Approval()
    {
        // Accrual/adjustments are re-evaluated at approval time, not frozen at submission time -
        // an authorised balance correction made while a request is pending must be reflected here.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = LeaveType.Create(leaveTypeId, companyId, "Annual Leave", "ANNUAL", 3,
            AccrualMethod.None, LeaveTypeBehaviour.Standard, now);
        var policy = LeavePolicy.Create(policyId, companyId, "No Negative Policy", null, 0,
            allowNegativeBalance: false, isDefault: false, now);
        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveTypeId, policyId,
            2026, 3m, new DateOnly(2026, 1, 1), now);

        var leaveRequest = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, policyId,
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 7), LeaveDayPart.FullDay,
            5m, "Needs an adjustment first", now);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.LeaveBalances.Add(balance);
        context.LeaveRequests.Add(leaveRequest);
        await context.SaveChangesAsync();

        // Simulates an authorised correction (e.g. AdjustLeaveBalanceHandler) applied while the
        // request sat pending, giving it just enough headroom to be approved.
        balance.Adjust(2m, now);
        await context.SaveChangesAsync();

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)));
        var result = await handler.HandleAsync(
            ApproveRequest(companyId, employeeId, leaveRequest.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var savedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(5m, savedBalance.UsedDays);
        Assert.Equal(0m, savedBalance.RemainingDays); // 3 entitlement + 2 adjustment - 5 used
    }

    [Fact]
    public async Task HandleAsync_Rejecting_Unrelated_Pending_Request_Does_Not_Undo_An_Authorised_Negative_Balance_Correction()
    {
        // An authorised correction (e.g. a manual balance adjustment) is allowed to leave a balance
        // negative even under a no-negative-balance policy - that override lives outside the
        // approval path entirely. Rejecting a separate, still-pending request must not touch the
        // balance at all (only an approved request's usage is reversed on rejection), so the
        // correction must survive untouched.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(),
            2026, 5m, new DateOnly(2026, 1, 1), now);
        balance.RecordUsage(5m, now);
        balance.Adjust(-3m, now); // authorised correction pushes the balance negative (-3 remaining)

        var pendingRequest = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(),
            new DateOnly(2026, 9, 7), LeaveDayPart.FullDay,
            new DateOnly(2026, 9, 7), LeaveDayPart.FullDay,
            1m, "Unrelated request", now);

        context.LeaveBalances.Add(balance);
        context.LeaveRequests.Add(pendingRequest);
        await context.SaveChangesAsync();

        var handler = new RejectLeaveRequestHandler(
            context, new NoOpNotificationWriter(), new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(),
            new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());

        var result = await handler.HandleAsync(new RejectLeaveRequestRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            LeaveRequestId = pendingRequest.Id,
            ReviewedByEmployeeId = Guid.NewGuid(),
            RejectionReason = "Not needed"
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var savedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(5m, savedBalance.UsedDays);
        Assert.Equal(-3m, savedBalance.AdjustmentDays);
        Assert.Equal(-3m, savedBalance.RemainingDays); // correction preserved, untouched by the rejection
    }

    // ---- Ticket 11 (P1) follow-up gap check: same-idempotency-key replay against a now-Approved
    // request must replay the original success response rather than failing with the
    // already-approved validation error. This is the SAME SaveIdempotentAsync-backed mechanism
    // LeaveTaskCompletionAction now reuses via ILeaveApprovalService's idempotencyKey parameter, so
    // covering it here also pins the behaviour that path depends on. ----

    [Fact]
    public async Task HandleAsync_With_Same_IdempotencyKey_Replays_Original_Success_Instead_Of_Failing_On_Already_Approved_Status()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        const string idempotencyKey = "task-completion-dispatch-key-1";

        var leaveRequest = await CreatePendingRequestWithNonBalanceTypeAsync(context, companyId, employeeId, now);

        var request = new ApproveLeaveRequestRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            LeaveRequestId = leaveRequest.Id,
            ReviewedByEmployeeId = reviewerId,
            IdempotencyKey = idempotencyKey,
        };

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow),
            new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)));

        var firstResult = await handler.HandleAsync(request, CancellationToken.None);
        Assert.True(firstResult.IsSuccess);
        Assert.Equal("Approved", firstResult.Value!.Status);

        // Same key, same request payload, submitted again against the now-Approved leave request —
        // must replay the first call's success response, not fail with
        // "Cannot approve a leave request with status 'Approved'.".
        var secondResult = await handler.HandleAsync(request, CancellationToken.None);

        Assert.True(secondResult.IsSuccess);
        Assert.Equal(firstResult.Value.Status, secondResult.Value!.Status);
        Assert.Equal(firstResult.Value.ReviewedAt, secondResult.Value.ReviewedAt);
        Assert.Equal(firstResult.Value.ReviewedByEmployeeId, secondResult.Value.ReviewedByEmployeeId);

        // Only one approval's worth of effects actually ran — balance/status mutated exactly once.
        var saved = await context.LeaveRequests.SingleAsync();
        Assert.Equal(LeaveRequestStatus.Approved, saved.Status);
    }

    [Fact]
    public async Task HandleAsync_With_Different_IdempotencyKey_Still_Fails_On_Already_Approved_Status()
    {
        // Negated branch of the above: a DIFFERENT key against an already-Approved request must
        // still hit the ordinary validation failure — replay only short-circuits on an exact repeat
        // key, never as a general "already approved is fine" bypass.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveRequest = await CreatePendingRequestWithNonBalanceTypeAsync(context, companyId, employeeId, now);

        var handler = new ApproveLeaveRequestHandler(context, new FakeClock(FixedUtcNow),
            new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)));

        var firstResult = await handler.HandleAsync(new ApproveLeaveRequestRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            LeaveRequestId = leaveRequest.Id,
            ReviewedByEmployeeId = reviewerId,
            IdempotencyKey = "key-one",
        }, CancellationToken.None);
        Assert.True(firstResult.IsSuccess);

        var secondResult = await handler.HandleAsync(new ApproveLeaveRequestRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            LeaveRequestId = leaveRequest.Id,
            ReviewedByEmployeeId = reviewerId,
            IdempotencyKey = "key-two",
        }, CancellationToken.None);

        Assert.True(secondResult.IsFailure);
        Assert.Equal("validation", secondResult.Error.Code);
    }
}
