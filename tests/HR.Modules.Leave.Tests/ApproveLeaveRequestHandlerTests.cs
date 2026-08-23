using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.ApproveLeaveRequest;
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
        var handler = new ApproveLeaveRequestHandler(context, new NoOpNotificationWriter(), new FakeClock(approvalTime), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
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
        var handler = new ApproveLeaveRequestHandler(context, new NoOpNotificationWriter(), new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), auditPublisher);

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
        var handler = new ApproveLeaveRequestHandler(context, new NoOpNotificationWriter(), new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());

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

        var handler = new ApproveLeaveRequestHandler(context, new NoOpNotificationWriter(), new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
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

        var handler = new ApproveLeaveRequestHandler(context, new NoOpNotificationWriter(), new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
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

        var handler = new ApproveLeaveRequestHandler(context, new NoOpNotificationWriter(), new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
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

        var handler = new ApproveLeaveRequestHandler(context, new NoOpNotificationWriter(), new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
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

        var handler = new ApproveLeaveRequestHandler(context, new NoOpNotificationWriter(), new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
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

        var handler = new ApproveLeaveRequestHandler(context, new NoOpNotificationWriter(), new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
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

        var handler = new ApproveLeaveRequestHandler(context, new NoOpNotificationWriter(), new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
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

        var handler = new ApproveLeaveRequestHandler(context, new NoOpNotificationWriter(), new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
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
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = LeaveType.Create(leaveTypeId, companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, now);

        var leaveRequest = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(),
            new DateOnly(2027, 1, 5), LeaveDayPart.FullDay,
            new DateOnly(2027, 1, 6), LeaveDayPart.FullDay,
            2m, "New year holiday", now);

        // Only the 2027 balance exists — a 2026 (today's) balance would produce the wrong deduction.
        var balance2027 = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(), 2027, 25m, now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveRequests.Add(leaveRequest);
        context.LeaveBalances.Add(balance2027);
        await context.SaveChangesAsync();

        var handler = new ApproveLeaveRequestHandler(context, new NoOpNotificationWriter(), new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
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

        var handler = new ApproveLeaveRequestHandler(context, new NoOpNotificationWriter(), new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
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

        var handler = new ApproveLeaveRequestHandler(context, new NoOpNotificationWriter(), new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
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

        var handler = new ApproveLeaveRequestHandler(context, new NoOpNotificationWriter(), new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
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

        var handler = new ApproveLeaveRequestHandler(context, new NoOpNotificationWriter(), new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
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

        var leaveRequest = await CreatePendingRequestWithNonBalanceTypeAsync(context, companyId, employeeId, now);

        var publisher = new CapturingIntegrationEventPublisher();
        var approvalTime = new DateTime(2026, 6, 13, 10, 0, 0, DateTimeKind.Utc);
        var handler = new ApproveLeaveRequestHandler(context, new NoOpNotificationWriter(), new FakeClock(approvalTime), publisher, new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
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
        var handler = new ApproveLeaveRequestHandler(context, new NoOpNotificationWriter(), new FakeClock(approvalTime), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), auditPublisher);
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

        var handler = new ApproveLeaveRequestHandler(context, notif, new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
        await handler.HandleAsync(ApproveRequest(companyId, employeeId, leaveRequest.Id, reviewerId), CancellationToken.None);

        var written = Assert.Single(notif.Written);
        Assert.Equal(companyId,                     written.CompanyId);
        Assert.Equal(employeeId,                    written.EmployeeId);
        Assert.Equal(leaveRequest.Id,               written.SourceEntityId);
        Assert.Equal(NotificationType.LeaveApproved, written.Type);
        Assert.Equal(NotificationPriority.Normal,    written.Priority);
    }
}
