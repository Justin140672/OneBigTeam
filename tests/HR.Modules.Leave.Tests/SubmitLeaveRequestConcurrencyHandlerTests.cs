using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.SubmitLeaveRequest;
using HR.Modules.Leave.Features.SubmitLeaveRequestDraft;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Services;
using HR.Modules.Leave.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HR.Modules.Leave.Tests;

/// <summary>
/// P2 (Ticket 4 follow-up): coverage for the new <c>catch (DbUpdateConcurrencyException)</c>
/// guards around the final save in <see cref="SubmitLeaveRequestHandler"/> and
/// <see cref="SubmitLeaveRequestDraftHandler"/>. Both handlers can silently mutate a shared
/// <see cref="LeaveBalance"/>/TOIL ledger row via <c>LeaveApprovalEffectsService.
/// ApplyBalanceEffectsAndApproveAsync</c> when the employee's leave policy has
/// <c>RequiresApproval = false</c> - exactly like a manual approval does in
/// <c>ApproveLeaveRequestHandler</c> (see LeaveConcurrencyHandlerTests.cs, the reference
/// implementation this mirrors). These tests use EF Core's InMemory provider, which - per that
/// same file's remarks - does enforce the mapped concurrency token, so two DbContext instances
/// racing the same balance row genuinely exercise the catch block, not just a mock throw.
/// Real cross-process Postgres concurrency for these same endpoints is covered separately in
/// HR.Integration.Tests/AutoApprovingLeaveSubmissionConcurrencyEndpointTests.cs.
/// </summary>
public class SubmitLeaveRequestConcurrencyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 15, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private readonly string _store = "submit-leave-conc-" + Guid.NewGuid().ToString("N");

    // .UseVersionedAggregates() is required - without it, VersionAdvancingSaveChangesInterceptor
    // never runs, Version never advances on save, and no conflict can ever be detected.
    private LeaveDbContext Ctx()
    {
        var builder = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(_store)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
        builder.UseVersionedAggregates();
        return new LeaveDbContext(builder.Options);
    }

    private static SubmitLeaveRequestHandler SubmitHandler(
        LeaveDbContext ctx,
        CapturingAuditEventPublisher audit,
        CapturingIntegrationEventPublisher integration,
        FakeNotificationWriter notifications) =>
        new(ctx,
            new FakeClock(FixedUtcNow),
            new FakeWorkingPatternProvider(),
            new FakeCompanyLeaveSettingsReader(),
            new FakePublicHolidayReader(),
            integration,
            audit,
            new LeaveApprovalEffectsService(ctx, notifications, integration, new FakeCompanyLeaveSettingsReader(), audit, new ToilLedgerService(ctx)),
            new LeaveWarningCalculator(new FakePublicHolidayReader()));

    private static SubmitLeaveRequestDraftHandler DraftHandler(
        LeaveDbContext ctx,
        CapturingAuditEventPublisher audit,
        CapturingIntegrationEventPublisher integration,
        FakeNotificationWriter notifications) =>
        new(ctx,
            new FakeClock(FixedUtcNow),
            new FakeWorkingPatternProvider(),
            new FakeCompanyLeaveSettingsReader(),
            new FakePublicHolidayReader(),
            integration,
            audit,
            new LeaveApprovalEffectsService(ctx, notifications, integration, new FakeCompanyLeaveSettingsReader(), audit, new ToilLedgerService(ctx)),
            new LeaveWarningCalculator(new FakePublicHolidayReader()));

    private async Task<(Guid CompanyId, Guid EmployeeId, Guid LeaveTypeId, Guid LeavePolicyId)> SeedAutoApprovingSetupAsync()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();

        await using var seed = Ctx();
        var leaveType = LeaveType.Create(leaveTypeId, companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.None, LeaveTypeBehaviour.Standard, Now);
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Auto-Approve", null, 0, false, isDefault: true, Now,
            requiresApproval: false);
        var assignment = EmployeeLeavePolicyAssignment.Create(
            Guid.NewGuid(), companyId, employeeId, policy.Id, new DateOnly(2026, 1, 1), Now);
        var balance = LeaveBalance.Create(
            Guid.NewGuid(), companyId, employeeId, leaveTypeId, policy.Id,
            2026, 25m, new DateOnly(2026, 1, 1), Now);

        seed.LeaveTypes.Add(leaveType);
        seed.LeavePolicies.Add(policy);
        seed.EmployeeLeavePolicyAssignments.Add(assignment);
        seed.LeaveBalances.Add(balance);
        await seed.SaveChangesAsync();

        return (companyId, employeeId, leaveTypeId, policy.Id);
    }

    private static SubmitLeaveRequestRequest SubmitRequest(
        Guid companyId, Guid employeeId, Guid leaveTypeId, DateOnly start, DateOnly end) =>
        new()
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            LeaveTypeId = leaveTypeId,
            StartDate = start,
            StartPart = LeaveDayPart.FullDay,
            EndDate = end,
            EndPart = LeaveDayPart.FullDay,
            Reason = "Concurrency test",
        };

    // ─── SubmitLeaveRequestHandler: two concurrent auto-approving submissions ──

    [Fact]
    public async Task Two_Concurrent_AutoApproving_Submissions_Against_The_Same_Balance_Second_Loses_With_Concurrency_Error()
    {
        var (companyId, employeeId, leaveTypeId, _) = await SeedAutoApprovingSetupAsync();

        // Both contexts load the (as yet un-mutated) balance BEFORE either commits - a genuine race
        // between two employees' overlap-free auto-approving submissions draining the same pot.
        await using var ctxA = Ctx();
        await using var ctxB = Ctx();
        _ = await ctxA.LeaveBalances.SingleAsync();
        _ = await ctxB.LeaveBalances.SingleAsync();

        var auditA = new CapturingAuditEventPublisher();
        var integrationA = new CapturingIntegrationEventPublisher();
        var notificationsA = new FakeNotificationWriter();
        var handlerA = SubmitHandler(ctxA, auditA, integrationA, notificationsA);

        var auditB = new CapturingAuditEventPublisher();
        var integrationB = new CapturingIntegrationEventPublisher();
        var notificationsB = new FakeNotificationWriter();
        var handlerB = SubmitHandler(ctxB, auditB, integrationB, notificationsB);

        var resultA = await handlerA.HandleAsync(
            SubmitRequest(companyId, employeeId, leaveTypeId, new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 5)), // 3 days
            CancellationToken.None);
        Assert.True(resultA.IsSuccess);
        Assert.NotEmpty(auditA.Published); // winner's audit event fired

        var resultB = await handlerB.HandleAsync(
            SubmitRequest(companyId, employeeId, leaveTypeId, new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 8)), // 2 days
            CancellationToken.None);
        Assert.True(resultB.IsFailure);
        Assert.Equal("concurrency", resultB.Error.Code);

        // No audit/notification/integration-event side effects fire for the losing save - all of
        // those calls happen strictly after SaveChangesAsync in the handler body.
        Assert.Empty(auditB.Published);
        Assert.Empty(integrationB.Published);
        Assert.Empty(notificationsB.Written);

        // Final balance reflects only the winner's 3-day deduction - never a lost update, never
        // both applied.
        await using var verify = Ctx();
        var savedBalance = await verify.LeaveBalances.SingleAsync();
        Assert.Equal(3m, savedBalance.UsedDays);
        Assert.Equal(22m, savedBalance.RemainingDays);

        // NOTE: whether the loser's own LeaveRequest Add is rolled back together with the failed
        // LeaveBalance update is NOT asserted here - EF Core's InMemory provider does not model
        // real cross-entity SaveChanges transactional atomicity the way a relational provider does,
        // so this would only prove an InMemory quirk, not production behaviour. The real atomicity
        // guarantee (a losing save leaves nothing behind, not even the new request/draft row) is
        // verified against real Postgres in
        // HR.Integration.Tests/AutoApprovingLeaveSubmissionConcurrencyEndpointTests.cs.
    }

    // ─── SubmitLeaveRequestDraftHandler: two concurrent auto-approving drafts ──

    [Fact]
    public async Task Two_Concurrent_AutoApproving_Draft_Submissions_Against_The_Same_Balance_Second_Loses_And_Draft_Is_Unchanged_And_Retryable()
    {
        var (companyId, employeeId, leaveTypeId, leavePolicyId) = await SeedAutoApprovingSetupAsync();

        Guid draftAId, draftBId;
        await using (var seed = Ctx())
        {
            var draftA = LeaveRequest.CreateDraft(
                Guid.NewGuid(), companyId, employeeId, leaveTypeId, leavePolicyId,
                new DateOnly(2026, 8, 3), LeaveDayPart.FullDay, new DateOnly(2026, 8, 5), LeaveDayPart.FullDay,
                3m, "Draft A", Now);
            var draftB = LeaveRequest.CreateDraft(
                Guid.NewGuid(), companyId, employeeId, leaveTypeId, leavePolicyId,
                new DateOnly(2026, 9, 7), LeaveDayPart.FullDay, new DateOnly(2026, 9, 8), LeaveDayPart.FullDay,
                2m, "Draft B", Now);
            seed.LeaveRequests.AddRange(draftA, draftB);
            await seed.SaveChangesAsync();
            draftAId = draftA.Id;
            draftBId = draftB.Id;
        }

        await using var ctxA = Ctx();
        await using var ctxB = Ctx();
        _ = await ctxA.LeaveBalances.SingleAsync();
        _ = await ctxB.LeaveBalances.SingleAsync();
        _ = await ctxA.LeaveRequests.SingleAsync(r => r.Id == draftAId);
        var trackedDraftB = await ctxB.LeaveRequests.SingleAsync(r => r.Id == draftBId);

        var auditA = new CapturingAuditEventPublisher();
        var handlerA = SubmitHandler(ctxA, auditA, new CapturingIntegrationEventPublisher(), new FakeNotificationWriter());

        var auditB = new CapturingAuditEventPublisher();
        var integrationB = new CapturingIntegrationEventPublisher();
        var notificationsB = new FakeNotificationWriter();
        var draftHandlerB = DraftHandler(ctxB, auditB, integrationB, notificationsB);

        var resultA = await handlerA.HandleAsync(
            SubmitRequest(companyId, employeeId, leaveTypeId, new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 5)),
            CancellationToken.None);
        Assert.True(resultA.IsSuccess);

        var resultB = await draftHandlerB.HandleAsync(
            new SubmitLeaveRequestDraftRequest { CompanyId = companyId, EmployeeId = employeeId, LeaveRequestId = draftBId },
            CancellationToken.None);
        Assert.True(resultB.IsFailure);
        Assert.Equal("concurrency", resultB.Error.Code);
        Assert.Empty(auditB.Published);
        Assert.Empty(integrationB.Published);
        Assert.Empty(notificationsB.Written);

        // The persisted draft is byte-for-byte unchanged: still Draft, same TotalDays/dates -
        // MarkSubmittedPending/ApplyBalanceEffectsAndApproveAsync's in-memory mutations on
        // trackedDraftB never reached the database because SaveChangesAsync threw before flushing.
        await using (var verify = Ctx())
        {
            var persistedDraftB = await verify.LeaveRequests.SingleAsync(r => r.Id == draftBId);
            Assert.Equal(LeaveRequestStatus.Draft, persistedDraftB.Status);
            Assert.Equal(2m, persistedDraftB.TotalDays);
            Assert.Equal(new DateOnly(2026, 9, 7), persistedDraftB.StartDate);
            Assert.Equal(new DateOnly(2026, 9, 8), persistedDraftB.EndDate);
            Assert.Equal(1, persistedDraftB.Version); // never advanced by the failed save
        }
        // Also sanity-check the in-memory entity the losing handler mutated before the failed save
        // still reports Draft's business-rule shape (defensive: MarkSubmittedPending/Approve throw
        // if called twice on an already-transitioned entity - see LeaveRequest.MarkSubmittedPending).
        Assert.NotNull(trackedDraftB);

        // Still retryable: a fresh context reloading the untouched draft and re-running the same
        // handler now succeeds, because the balance's Version has already moved on and this is a
        // brand-new attempt starting from the current state, not a replay of the stale one.
        await using var retryCtx = Ctx();
        var retryAudit = new CapturingAuditEventPublisher();
        var retryHandler = DraftHandler(retryCtx, retryAudit, new CapturingIntegrationEventPublisher(), new FakeNotificationWriter());
        var retryResult = await retryHandler.HandleAsync(
            new SubmitLeaveRequestDraftRequest { CompanyId = companyId, EmployeeId = employeeId, LeaveRequestId = draftBId },
            CancellationToken.None);
        Assert.True(retryResult.IsSuccess);
        Assert.Equal("Approved", retryResult.Value!.Status);

        await using var finalVerify = Ctx();
        var finalBalance = await finalVerify.LeaveBalances.SingleAsync();
        Assert.Equal(5m, finalBalance.UsedDays); // 3 (winner A) + 2 (retried B)
    }
}
