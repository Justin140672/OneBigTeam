using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.ApproveLeaveRequest;
using HR.Modules.Leave.Features.AwardToil;
using HR.Modules.Leave.Features.CancelLeaveRequest;
using HR.Modules.Leave.Features.RejectLeaveRequest;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Services;
using HR.Modules.Leave.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HR.Modules.Leave.Tests;

/// <summary>
/// P1 #4 (optimistic concurrency): LeaveBalance.Version / LeaveRequest.Version coverage.
///
/// Unlike the ExpectedVersion-based Ticket 2 handlers (e.g. UpdateLeaveType, which compare a
/// client-supplied version before saving), Approve/Reject/Cancel/AwardToil/AdjustLeaveBalance rely
/// entirely on EF's own concurrency-token check (IsConcurrencyToken() on the mapped `version`
/// column) plus the shared VersionAdvancingSaveChangesInterceptor. EF Core's InMemory provider DOES
/// enforce concurrency tokens (see HR.SharedKernel.Tests/VersionAdvancingSaveChangesInterceptorTests
/// and HR.Modules.Companies.Tests/StripeWebhookConcurrencyTests for existing precedent), so these
/// tests genuinely exercise two DbContext instances racing the same row and prove the handlers'
/// catch (DbUpdateConcurrencyException) branches translate a real EF-detected conflict into
/// Result.Failure(Error.Concurrency(...)) - not just that the code compiles. Real cross-process
/// Postgres concurrency (two actual HTTP requests racing) is covered separately in
/// HR.Integration.Tests/LeaveConcurrencyEndpointTests.cs.
/// </summary>
public class LeaveConcurrencyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 15, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private readonly string _store = "leave-conc-" + Guid.NewGuid().ToString("N");

    // .UseVersionedAggregates() is required here - without it, VersionAdvancingSaveChangesInterceptor
    // is never registered, Version never advances on save, and no conflict can ever be detected.
    // ConfigureWarnings ignores InMemory's "transactions not supported" warning (which EF otherwise
    // throws as an error) - AdjustLeaveBalanceHandler opens an explicit transaction; InMemory just
    // no-ops it, which is fine for what these tests assert.
    private LeaveDbContext Ctx()
    {
        var builder = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(_store)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
        builder.UseVersionedAggregates();
        return new LeaveDbContext(builder.Options);
    }

    // ─── Domain-level: IVersionedAggregate wiring ─────────────────────────────

    [Fact]
    public void LeaveBalance_Version_Starts_At_One_And_IncrementVersion_Increments_By_One()
    {
        var balance = LeaveBalance.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            2026, 25m, new DateOnly(2026, 1, 1), Now);

        Assert.Equal(1, balance.Version);

        balance.IncrementVersion();
        Assert.Equal(2, balance.Version);

        balance.IncrementVersion();
        Assert.Equal(3, balance.Version);
    }

    [Fact]
    public void LeaveRequest_Version_Starts_At_One_And_IncrementVersion_Increments_By_One()
    {
        var request = LeaveRequest.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 7), LeaveDayPart.FullDay,
            5m, "Holiday", Now);

        Assert.Equal(1, request.Version);

        request.IncrementVersion();
        Assert.Equal(2, request.Version);

        request.IncrementVersion();
        Assert.Equal(3, request.Version);
    }

    // ─── ApproveLeaveRequest vs ApproveLeaveRequest: two requests, same balance ─

    [Fact]
    public async Task Two_Concurrent_Approvals_Deducting_The_Same_Balance_Second_Loses_With_Concurrency_Error()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();

        Guid requestAId, requestBId;
        await using (var seed = Ctx())
        {
            var leaveType = LeaveType.Create(leaveTypeId, companyId, "Annual Leave", "ANNUAL", 25,
                AccrualMethod.None, LeaveTypeBehaviour.Standard, Now);
            var balance = LeaveBalance.Create(
                Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(),
                2026, 25m, new DateOnly(2026, 1, 1), Now);
            var requestA = LeaveRequest.Create(
                Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(),
                new DateOnly(2026, 8, 3), LeaveDayPart.FullDay, new DateOnly(2026, 8, 5), LeaveDayPart.FullDay,
                3m, "First", Now);
            var requestB = LeaveRequest.Create(
                Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(),
                new DateOnly(2026, 9, 3), LeaveDayPart.FullDay, new DateOnly(2026, 9, 4), LeaveDayPart.FullDay,
                2m, "Second", Now);

            seed.LeaveTypes.Add(leaveType);
            seed.LeaveBalances.Add(balance);
            seed.LeaveRequests.AddRange(requestA, requestB);
            await seed.SaveChangesAsync();

            requestAId = requestA.Id;
            requestBId = requestB.Id;
        }

        // Both contexts load the balance/requests BEFORE either commits, simulating a genuine race.
        await using var ctxA = Ctx();
        await using var ctxB = Ctx();
        _ = await ctxA.LeaveBalances.SingleAsync();
        _ = await ctxB.LeaveBalances.SingleAsync();

        var handlerA = ApproveHandler(ctxA);
        var handlerB = ApproveHandler(ctxB);

        var resultA = await handlerA.HandleAsync(ApproveRequest(companyId, employeeId, requestAId), CancellationToken.None);
        Assert.True(resultA.IsSuccess);

        var resultB = await handlerB.HandleAsync(ApproveRequest(companyId, employeeId, requestBId), CancellationToken.None);
        Assert.True(resultB.IsFailure);
        Assert.Equal("concurrency", resultB.Error.Code);

        // Final state reflects ONLY the winner's deduction - not a lost update, not both applied.
        await using var verify = Ctx();
        var savedBalance = await verify.LeaveBalances.SingleAsync();
        Assert.Equal(3m, savedBalance.UsedDays);
        Assert.Equal(22m, savedBalance.RemainingDays);

        var savedRequestA = await verify.LeaveRequests.SingleAsync(r => r.Id == requestAId);
        var savedRequestB = await verify.LeaveRequests.SingleAsync(r => r.Id == requestBId);
        Assert.Equal(LeaveRequestStatus.Approved, savedRequestA.Status);
        Assert.Equal(LeaveRequestStatus.Pending, savedRequestB.Status); // loser's approve never committed
    }

    // ─── Approve vs Reject: competing status transitions on the SAME request ──

    [Fact]
    public async Task Concurrent_Approve_And_Reject_Of_The_Same_Request_Produce_One_Consistent_Outcome()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        Guid requestId;

        await using (var seed = Ctx())
        {
            var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Unpaid", "UNPAID", 0,
                AccrualMethod.None, LeaveTypeBehaviour.Standard, Now, hasBalance: false);
            var request = LeaveRequest.Create(
                Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(),
                new DateOnly(2026, 8, 3), LeaveDayPart.FullDay, new DateOnly(2026, 8, 5), LeaveDayPart.FullDay,
                3m, "Holiday", Now);
            seed.LeaveTypes.Add(leaveType);
            seed.LeaveRequests.Add(request);
            await seed.SaveChangesAsync();
            requestId = request.Id;
        }

        await using var approveCtx = Ctx();
        await using var rejectCtx = Ctx();
        _ = await approveCtx.LeaveRequests.SingleAsync();
        _ = await rejectCtx.LeaveRequests.SingleAsync();

        // Approve commits first, advancing Version.
        var approveResult = await ApproveHandler(approveCtx)
            .HandleAsync(ApproveRequest(companyId, employeeId, requestId), CancellationToken.None);
        Assert.True(approveResult.IsSuccess);

        // Reject's tracked entity still has the stale, originally-loaded Version.
        var rejectResult = await RejectHandler(rejectCtx)
            .HandleAsync(RejectRequest(companyId, employeeId, requestId), CancellationToken.None);
        Assert.True(rejectResult.IsFailure);
        Assert.Equal("concurrency", rejectResult.Error.Code);

        await using var verify = Ctx();
        var saved = await verify.LeaveRequests.SingleAsync();
        Assert.Equal(LeaveRequestStatus.Approved, saved.Status); // never corrupted/ambiguous
    }

    // ─── Approve vs Cancel: competing status transitions on the SAME request ──

    [Fact]
    public async Task Concurrent_Approve_And_Cancel_Of_The_Same_Request_Produce_One_Consistent_Outcome()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        Guid requestId;

        await using (var seed = Ctx())
        {
            var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Unpaid", "UNPAID", 0,
                AccrualMethod.None, LeaveTypeBehaviour.Standard, Now, hasBalance: false);
            var request = LeaveRequest.Create(
                Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(),
                new DateOnly(2026, 8, 3), LeaveDayPart.FullDay, new DateOnly(2026, 8, 5), LeaveDayPart.FullDay,
                3m, "Holiday", Now);
            seed.LeaveTypes.Add(leaveType);
            seed.LeaveRequests.Add(request);
            await seed.SaveChangesAsync();
            requestId = request.Id;
        }

        await using var cancelCtx = Ctx();
        await using var approveCtx = Ctx();
        _ = await cancelCtx.LeaveRequests.SingleAsync();
        _ = await approveCtx.LeaveRequests.SingleAsync();

        var cancelResult = await CancelHandler(cancelCtx)
            .HandleAsync(CancelRequest(companyId, employeeId, requestId), CancellationToken.None);
        Assert.True(cancelResult.IsSuccess);

        var approveResult = await ApproveHandler(approveCtx)
            .HandleAsync(ApproveRequest(companyId, employeeId, requestId), CancellationToken.None);
        Assert.True(approveResult.IsFailure);
        Assert.Equal("concurrency", approveResult.Error.Code);

        await using var verify = Ctx();
        var saved = await verify.LeaveRequests.SingleAsync();
        Assert.Equal(LeaveRequestStatus.Cancelled, saved.Status);
    }

    // ─── AwardToil vs AwardToil: two concurrent awards to the SAME balance row ─

    [Fact]
    public async Task Two_Concurrent_Toil_Awards_To_The_Same_Balance_Second_Loses_With_Concurrency_Error()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using (var seed = Ctx())
        {
            var toilType = LeaveType.Create(Guid.NewGuid(), companyId, "TOIL", "TOIL", 0,
                AccrualMethod.None, LeaveTypeBehaviour.Toil, Now);
            var assignment = EmployeeLeavePolicyAssignment.Create(
                Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(), new DateOnly(2026, 1, 1), Now);
            seed.LeaveTypes.Add(toilType);
            seed.EmployeeLeavePolicyAssignments.Add(assignment);
            await seed.SaveChangesAsync();

            // Establish the balance row first via a normal, uncontested award - two concurrent
            // awards against a not-yet-existing balance would each insert their own distinct row
            // (nothing to race on), so this seed step ensures both racing awards below hit the
            // shared balance.Adjust(...) UPDATE path AwardToilHandler is actually exposed to once a
            // balance already exists for the policy year.
            var seedHandler = new AwardToilHandler(seed, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
            var seedResult = await seedHandler.HandleAsync(
                new AwardToilRequest { CompanyId = companyId, EmployeeId = employeeId, AwardedByEmployeeId = Guid.NewGuid(), Days = 1m, OccurredOn = new DateOnly(2026, 5, 1) },
                CancellationToken.None);
            Assert.True(seedResult.IsSuccess);
        }

        await using var ctxA = Ctx();
        await using var ctxB = Ctx();
        // Both contexts load the existing balance BEFORE either commits, simulating a genuine race.
        _ = await ctxA.LeaveBalances.SingleAsync();
        _ = await ctxB.LeaveBalances.SingleAsync();

        var handlerA = new AwardToilHandler(ctxA, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());
        var handlerB = new AwardToilHandler(ctxB, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());

        var requestA = new AwardToilRequest
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            AwardedByEmployeeId = Guid.NewGuid(),
            Days = 2m,
            OccurredOn = new DateOnly(2026, 6, 1),
        };
        var requestB = requestA with { Days = 3m, OccurredOn = new DateOnly(2026, 6, 2) };

        var resultA = await handlerA.HandleAsync(requestA, CancellationToken.None);
        Assert.True(resultA.IsSuccess);

        var resultB = await handlerB.HandleAsync(requestB, CancellationToken.None);
        Assert.True(resultB.IsFailure);
        Assert.Equal("concurrency", resultB.Error.Code);

        await using var verify = Ctx();
        var savedBalance = await verify.LeaveBalances.SingleAsync();
        Assert.Equal(1m + 2m, savedBalance.AdjustmentDays); // seed + winning award only
    }

    // ─── AdjustLeaveBalance vs AdjustLeaveBalance: two concurrent manual edits ─

    [Fact]
    public async Task Two_Concurrent_Balance_Adjustments_Second_Loses_And_Rolls_Back()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();

        await using (var seed = Ctx())
        {
            var leaveType = LeaveType.Create(leaveTypeId, companyId, "Annual Leave", "ANNUAL", 25,
                AccrualMethod.None, LeaveTypeBehaviour.Standard, Now);
            var balance = LeaveBalance.Create(
                Guid.NewGuid(), companyId, employeeId, leaveTypeId, Guid.NewGuid(),
                DateTimeOffset.UtcNow.Year, 25m, new DateOnly(DateTimeOffset.UtcNow.Year, 1, 1), Now);
            seed.LeaveTypes.Add(leaveType);
            seed.LeaveBalances.Add(balance);
            await seed.SaveChangesAsync();
        }

        await using var ctxA = Ctx();
        await using var ctxB = Ctx();
        _ = await ctxA.LeaveBalances.SingleAsync();
        _ = await ctxB.LeaveBalances.SingleAsync();

        var names = new Dictionary<Guid, string> { [employeeId] = "Jane Doe" };
        var handlerA = new HR.Modules.Leave.Features.AdjustLeaveBalance.AdjustLeaveBalanceHandler(
            ctxA, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakeEmployeeNameReader(names));
        var handlerB = new HR.Modules.Leave.Features.AdjustLeaveBalance.AdjustLeaveBalanceHandler(
            ctxB, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(), new FakeCompanyLeaveSettingsReader(), new FakeEmployeeNameReader(names));

        var requestA = new HR.Modules.Leave.Features.AdjustLeaveBalance.AdjustLeaveBalanceRequest(
            companyId, employeeId, leaveTypeId, 2m, LeaveBalanceAdjustmentReason.Correction, "First", false)
        { AdjustedByEmployeeId = Guid.NewGuid() };
        var requestB = requestA with { AdjustmentValue = 3m, Comments = "Second" };

        var resultA = await handlerA.HandleAsync(requestA, CancellationToken.None);
        Assert.True(resultA.IsSuccess);

        var resultB = await handlerB.HandleAsync(requestB, CancellationToken.None);
        Assert.True(resultB.IsFailure);
        Assert.Equal("concurrency", resultB.Error.Code);

        // The balance value is the load-bearing assertion for this ticket: the loser's adjustment
        // must never be silently added on top of a stale-read balance (the lost-update this ticket
        // fixes). Note: EF Core's InMemory provider does not model true transactional atomicity
        // across the multiple entities written in one SaveChangesAsync call, so the sibling
        // LeaveBalanceAdjustment audit-trail row from the losing attempt may still be visible here
        // even though the transaction.RollbackAsync() was invoked and the balance itself was not
        // corrupted; that full-atomicity guarantee is covered by the real-Postgres tests in
        // HR.Integration.Tests/LeaveConcurrencyEndpointTests.cs, not here.
        await using var verify = Ctx();
        var savedBalance = await verify.LeaveBalances.SingleAsync();
        Assert.Equal(2m, savedBalance.AdjustmentDays); // only the winning adjustment applied
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    private static ApproveLeaveRequestHandler ApproveHandler(LeaveDbContext ctx) =>
        new(ctx, new FakeClock(FixedUtcNow),
            new LeaveApprovalEffectsService(ctx, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(),
                new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(ctx)));

    private static RejectLeaveRequestHandler RejectHandler(LeaveDbContext ctx) =>
        new(ctx, new NoOpNotificationWriter(), new FakeClock(FixedUtcNow), new NoOpIntegrationEventPublisher(),
            new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher());

    private static CancelLeaveRequestHandler CancelHandler(LeaveDbContext ctx) =>
        new(ctx, new FakeClock(FixedUtcNow), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(ctx));

    private static ApproveLeaveRequestRequest ApproveRequest(Guid companyId, Guid employeeId, Guid leaveRequestId) =>
        new() { CompanyId = companyId, EmployeeId = employeeId, LeaveRequestId = leaveRequestId, ReviewedByEmployeeId = Guid.NewGuid() };

    private static RejectLeaveRequestRequest RejectRequest(Guid companyId, Guid employeeId, Guid leaveRequestId) =>
        new() { CompanyId = companyId, EmployeeId = employeeId, LeaveRequestId = leaveRequestId, ReviewedByEmployeeId = Guid.NewGuid(), RejectionReason = "No cover" };

    private static CancelLeaveRequestRequest CancelRequest(Guid companyId, Guid employeeId, Guid leaveRequestId) =>
        new() { CompanyId = companyId, EmployeeId = employeeId, LeaveRequestId = leaveRequestId };
}
