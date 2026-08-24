using HR.Modules.Leave.Services;
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

    private static ToilTransaction CreateEarnedBucket(
        Guid companyId, Guid employeeId, Guid balanceId, decimal days, DateOnly occurredOn, DateTimeOffset now) =>
        ToilTransaction.CreateEarned(
            Guid.NewGuid(), companyId, employeeId, balanceId, Guid.NewGuid(), days, occurredOn, null, null, now);

    private static ToilTransaction CreateUsedTransaction(
        Guid companyId, Guid employeeId, Guid balanceId, Guid bucketId, Guid leaveRequestId, decimal days, DateOnly occurredOn, DateTimeOffset now) =>
        ToilTransaction.CreateUsed(
            Guid.NewGuid(), companyId, employeeId, balanceId, bucketId, leaveRequestId, Guid.NewGuid(), days, occurredOn, "TOIL used", now);

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
        var handler = new CancelLeaveRequestHandler(context, new FakeClock(cancelTime), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context));
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

        var handler = new CancelLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context));
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
        var handler = new CancelLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context));

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

        var handler = new CancelLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context));
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

        var handler = new CancelLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context));
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

        var handler = new CancelLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context));
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

        var handler = new CancelLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context));
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
            2026, 25m, new DateOnly(2026, 1, 1), now);
        balance.RecordUsage(5m, now);

        context.LeaveRequests.Add(leaveRequest);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new CancelLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context));
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
            2026, 25m, new DateOnly(2026, 1, 1), now);

        context.LeaveRequests.Add(leaveRequest);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        var handler = new CancelLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context));
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

        var handler = new CancelLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context));
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
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(), 2026, 0m, new DateOnly(2026, 1, 1), now);
        balance.Adjust(5m, now);
        balance.RecordUsage(3m, now);
        var bucket = CreateEarnedBucket(companyId, employeeId, balance.Id, 5m, new DateOnly(2026, 5, 1), now);
        var used = CreateUsedTransaction(companyId, employeeId, balance.Id, bucket.Id, leaveRequest.Id, 3m, new DateOnly(2026, 8, 3), now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveRequests.Add(leaveRequest);
        context.LeaveBalances.Add(balance);
        context.ToilTransactions.AddRange(bucket, used);
        await context.SaveChangesAsync();

        var handler = new CancelLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context));
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
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(), 2025, 0m, new DateOnly(2025, 1, 1), now);
        balance2025.Adjust(4m, now);
        balance2025.RecordUsage(3m, now);
        var bucket2025 = CreateEarnedBucket(companyId, employeeId, balance2025.Id, 4m, new DateOnly(2025, 6, 1), now);
        var used = CreateUsedTransaction(companyId, employeeId, balance2025.Id, bucket2025.Id, leaveRequest.Id, 3m, new DateOnly(2026, 1, 5), now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveRequests.Add(leaveRequest);
        context.LeaveBalances.Add(balance2025);
        context.ToilTransactions.AddRange(bucket2025, used);
        await context.SaveChangesAsync();

        var handler = new CancelLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context));
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
        var handler = new CancelLeaveRequestHandler(context, new FakeClock(cancelTime), new FakeCompanyLeaveSettingsReader(), auditPublisher, new ToilLedgerService(context));
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

    [Fact]
    public async Task HandleAsync_Cancelling_Approved_Toil_Request_Creates_Traceable_Reversal_Transactions()
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
        leaveRequest.Approve(Guid.NewGuid(), now);

        var balance = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(), 2026, 0m, new DateOnly(2026, 1, 1), now);
        balance.Adjust(6m, now);
        balance.RecordUsage(4m, now);

        // Two buckets were drawn from when the request was approved (mirroring what ApproveLeaveRequestHandler /
        // ToilLedgerService.ConsumeAsync would have produced for a 4-day request spanning two 2-day buckets).
        var bucketA = CreateEarnedBucket(companyId, employeeId, balance.Id, 2m, new DateOnly(2026, 4, 1), now);
        var bucketB = CreateEarnedBucket(companyId, employeeId, balance.Id, 4m, new DateOnly(2026, 5, 1), now);
        var usedA = CreateUsedTransaction(companyId, employeeId, balance.Id, bucketA.Id, leaveRequest.Id, 2m, new DateOnly(2026, 8, 3), now);
        var usedB = CreateUsedTransaction(companyId, employeeId, balance.Id, bucketB.Id, leaveRequest.Id, 2m, new DateOnly(2026, 8, 3), now);

        context.LeaveTypes.Add(leaveType);
        context.LeaveRequests.Add(leaveRequest);
        context.LeaveBalances.Add(balance);
        context.ToilTransactions.AddRange(bucketA, bucketB, usedA, usedB);
        await context.SaveChangesAsync();

        var handler = new CancelLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context));
        var result = await handler.HandleAsync(
            new CancelLeaveRequestRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                LeaveRequestId = leaveRequest.Id
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var reversals = await context.ToilTransactions
            .Where(t => t.Type == ToilTransactionType.Adjusted && t.ReversesTransactionId != null)
            .ToListAsync();
        Assert.Equal(2, reversals.Count);

        var reversalOfA = reversals.Single(r => r.ReversesTransactionId == usedA.Id);
        var reversalOfB = reversals.Single(r => r.ReversesTransactionId == usedB.Id);
        Assert.Equal(bucketA.Id, reversalOfA.RelatedTransactionId);
        Assert.Equal(2m, reversalOfA.Days);
        Assert.Equal(bucketB.Id, reversalOfB.RelatedTransactionId);
        Assert.Equal(2m, reversalOfB.Days);

        var savedBalance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(0m, savedBalance.UsedDays);
        Assert.Equal(6m, savedBalance.RemainingDays);

        // Subsequent consumption should be able to draw straight back from the restored buckets,
        // confirming the reversal correctly restored the ledger rather than just the aggregate.
        var ledgerService = new ToilLedgerService(context);
        var reconsumeResult = await ledgerService.ConsumeAsync(
            companyId, employeeId, leaveType.Id, 2m, Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 9, 1), allowNegativeBalance: false, now, CancellationToken.None);

        Assert.True(reconsumeResult.IsSuccess);
        var reconsumed = Assert.Single(reconsumeResult.Value!.Transactions);
        Assert.Equal(bucketA.Id, reconsumed.RelatedTransactionId); // oldest bucket again, FIFO
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Cancelling_A_Draft()
    {
        // LEAVE-07: a Draft was never submitted, so cancel is not a meaningful action - the caller
        // must use DeleteLeaveRequestDraft instead.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var draft = LeaveRequest.CreateDraft(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(), null,
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 7), LeaveDayPart.FullDay,
            5m, "Holiday", now);
        context.LeaveRequests.Add(draft);
        await context.SaveChangesAsync();

        var handler = new CancelLeaveRequestHandler(context, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context));
        var result = await handler.HandleAsync(
            new CancelLeaveRequestRequest
            {
                CompanyId = companyId,
                EmployeeId = employeeId,
                LeaveRequestId = draft.Id
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);

        var saved = await context.LeaveRequests.SingleAsync();
        Assert.Equal(LeaveRequestStatus.Draft, saved.Status);
    }
}
