using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Jobs;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Identity.Tests.Jobs;

// P1 fix (departure access disablement): unit tests for AccountDisablementJob, the durable,
// retryable worker that performs and confirms the actual ApplicationUser.IsActive disablement
// requested by Features/OnEmployeeDepartureFinalised. Mirrors
// HR.Modules.Companies.Tests.EmployeeRenumberSideEffectJobTests's retry/attempt-tracking pattern.
[Collection("IdentityDatabase")]
public class AccountDisablementJobTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 9, 11, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 9, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    // Only publishes audit events once the AccountDisablement row it's checking against is already
    // Processed in a *freshly-loaded* context — proves the job never reports success optimistically.
    private sealed class OrderVerifyingAuditEventPublisher(IdentityDatabaseFixture fixture, Guid accountDisablementId)
        : IAuditEventPublisher
    {
        public List<object> PublishedEvents { get; } = [];
        public bool RowWasAlreadyProcessedAtPublishTime { get; private set; }

        public async Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken)
        {
            await using var db = fixture.BuildContext();
            var row = await db.AccountDisablements.SingleAsync(d => d.Id == accountDisablementId, cancellationToken);
            RowWasAlreadyProcessedAtPublishTime = row.Status == AccountDisablement.StatusProcessed;

            PublishedEvents.Add(auditEvent!);
        }
    }

    private static AccountDisablementJob BuildJob(
        IdentityDbContext db, IAuditEventPublisher auditEventPublisher) =>
        new(db, Clock, auditEventPublisher, NullLogger<AccountDisablementJob>.Instance);

    private async Task<(Guid CompanyId, Guid EmployeeId, AccountDisablement Request)> SeedPendingRequestAsync(
        bool userActive = true)
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var applicationUserId = employeeId;

        await using var db = fixture.BuildContext();
        var user = ApplicationUser.Create(applicationUserId, $"{Guid.NewGuid():N}@test.com", "hash", "First", "Last", RequestedAt);
        if (!userActive)
            user.Deactivate(RequestedAt);
        db.Users.Add(user);

        var request = AccountDisablement.CreatePending(
            Guid.NewGuid(), companyId, applicationUserId, employeeId, RequestedAt);
        db.AccountDisablements.Add(request);

        await db.SaveChangesAsync();
        return (companyId, employeeId, request);
    }

    [Fact]
    public async Task ProcessAsync_Happy_Path_Deactivates_User_Marks_Processed_And_Publishes_Audit_After_Save()
    {
        var (companyId, _, request) = await SeedPendingRequestAsync(userActive: true);

        var auditPublisher = new OrderVerifyingAuditEventPublisher(fixture, request.Id);
        await using var db = fixture.BuildContext();
        var job = BuildJob(db, auditPublisher);

        await job.ProcessAsync(request.Id, companyId);

        var reloadedUser = await db.Users.SingleAsync(u => u.Id == request.ApplicationUserId);
        Assert.False(reloadedUser.IsActive);

        var reloadedRequest = await db.AccountDisablements.SingleAsync(d => d.Id == request.Id);
        Assert.Equal(AccountDisablement.StatusProcessed, reloadedRequest.Status);
        Assert.NotNull(reloadedRequest.ProcessedAt);

        Assert.True(auditPublisher.RowWasAlreadyProcessedAtPublishTime);
        Assert.Single(auditPublisher.PublishedEvents, e => e.GetType().Name == "UserAutoDisabledOnDepartureAuditEvent");
    }

    [Fact]
    public async Task ProcessAsync_Does_Not_Throw_When_AccountDisablement_Row_Is_Missing()
    {
        await using var db = fixture.BuildContext();
        var auditPublisher = new FakeAuditEventPublisher();
        var job = BuildJob(db, auditPublisher);

        var exception = await Record.ExceptionAsync(() => job.ProcessAsync(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Null(exception);
        Assert.Empty(auditPublisher.PublishedEvents);
    }

    [Fact]
    public async Task ProcessAsync_Throws_When_Supplied_CompanyId_Does_Not_Match_Request()
    {
        var (companyId, _, request) = await SeedPendingRequestAsync();
        var otherCompanyId = Guid.NewGuid();
        Assert.NotEqual(companyId, otherCompanyId);

        await using var db = fixture.BuildContext();
        var auditPublisher = new FakeAuditEventPublisher();
        var job = BuildJob(db, auditPublisher);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ProcessAsync(request.Id, otherCompanyId));
        Assert.Empty(auditPublisher.PublishedEvents);
    }

    [Fact]
    public async Task ProcessAsync_Is_NoOp_When_Already_Processed()
    {
        var (companyId, _, request) = await SeedPendingRequestAsync();

        await using (var seedDb = fixture.BuildContext())
        {
            var toComplete = await seedDb.AccountDisablements.SingleAsync(d => d.Id == request.Id);
            toComplete.MarkProcessing(RequestedAt.AddMinutes(1));
            toComplete.MarkProcessed(RequestedAt.AddMinutes(2));
            await seedDb.SaveChangesAsync();
        }

        await using var db = fixture.BuildContext();
        var auditPublisher = new FakeAuditEventPublisher();
        var job = BuildJob(db, auditPublisher);

        await job.ProcessAsync(request.Id, companyId);

        Assert.Empty(auditPublisher.PublishedEvents);
        var reloaded = await db.AccountDisablements.SingleAsync(d => d.Id == request.Id);
        Assert.Equal(RequestedAt.AddMinutes(2), reloaded.ProcessedAt); // untouched — no second run occurred
    }

    [Fact]
    public async Task ProcessAsync_User_Already_Inactive_Still_Marks_Processed_Without_Double_Deactivating()
    {
        var (companyId, _, request) = await SeedPendingRequestAsync(userActive: false);

        await using var db = fixture.BuildContext();
        var auditPublisher = new FakeAuditEventPublisher();
        var job = BuildJob(db, auditPublisher);

        await job.ProcessAsync(request.Id, companyId);

        var reloadedUser = await db.Users.SingleAsync(u => u.Id == request.ApplicationUserId);
        Assert.False(reloadedUser.IsActive);

        var reloadedRequest = await db.AccountDisablements.SingleAsync(d => d.Id == request.Id);
        Assert.Equal(AccountDisablement.StatusProcessed, reloadedRequest.Status);

        Assert.Single(auditPublisher.PublishedEvents, e => e.GetType().Name == "UserAutoDisabledOnDepartureAuditEvent");
    }

    [Fact]
    public async Task ProcessAsync_Marks_Processed_Without_Exception_When_Linked_User_Is_Missing()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using (var seedDb = fixture.BuildContext())
        {
            // No ApplicationUser seeded — the linked account no longer exists.
            var request = AccountDisablement.CreatePending(
                Guid.NewGuid(), companyId, Guid.NewGuid(), employeeId, RequestedAt);
            seedDb.AccountDisablements.Add(request);
            await seedDb.SaveChangesAsync();

            var auditPublisher = new FakeAuditEventPublisher();
            var job = BuildJob(seedDb, auditPublisher);

            var exception = await Record.ExceptionAsync(() => job.ProcessAsync(request.Id, companyId));

            Assert.Null(exception);
            var reloaded = await seedDb.AccountDisablements.SingleAsync(d => d.Id == request.Id);
            Assert.Equal(AccountDisablement.StatusProcessed, reloaded.Status);
            Assert.Empty(auditPublisher.PublishedEvents);
        }
    }

    // Fault injection strategy: IClock is the only collaborator inside AccountDisablementJob's try
    // block that can be made to fail without corrupting the DbContext directly — SelectiveThrowingClock
    // throws on exactly its Nth access. The 1st access (before the try block, for MarkProcessing) is
    // left to succeed so AttemptCount tracking behaves normally; the 2nd access (inside the try,
    // fetching/deactivating the user) is made to throw, simulating a genuine mid-operation failure
    // before the row would otherwise be marked Processed.
    [Fact]
    public async Task ProcessAsync_Transient_Failure_Increments_AttemptCount_Leaves_Processing_And_Rethrows()
    {
        var (companyId, _, request) = await SeedPendingRequestAsync();

        await using var db = fixture.BuildContext();
        var auditPublisher = new FakeAuditEventPublisher();
        var throwingClock = new SelectiveThrowingClock(Now.UtcDateTime, throwOnCallNumber: 2);
        var job = new AccountDisablementJob(db, throwingClock, auditPublisher, NullLogger<AccountDisablementJob>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ProcessAsync(request.Id, companyId));

        var reloaded = await db.AccountDisablements.SingleAsync(d => d.Id == request.Id);
        // AttemptCount is 1 (MarkProcessing ran once) but this isn't the final attempt
        // (MaxAttempts=4), so the row is left Processing rather than Failed.
        Assert.Equal(1, reloaded.AttemptCount);
        Assert.Equal(AccountDisablement.StatusProcessing, reloaded.Status);
        Assert.Null(reloaded.FailureReason);
        Assert.Empty(auditPublisher.PublishedEvents);
    }

    [Fact]
    public async Task ProcessAsync_Final_Attempt_Failure_Marks_Failed_With_Reason_Publishes_Audit_And_Rethrows()
    {
        var (companyId, _, request) = await SeedPendingRequestAsync();

        // Simplification (per test-plan note): rather than driving AttemptCount up via repeated
        // real invocations (which, given AccountDisablementJob's actual save-before-publish
        // ordering, would already leave the row Processed after the first "failure" and short-
        // circuit further attempts via the Status==Processed idempotency guard), seed AttemptCount
        // directly to MaxAttempts-1 via the domain's own MarkProcessing/SaveChanges, then trigger
        // exactly one more (final) failing attempt.
        await using (var seedDb = fixture.BuildContext())
        {
            var toBump = await seedDb.AccountDisablements.SingleAsync(d => d.Id == request.Id);
            for (var i = 0; i < AccountDisablementJob.MaxAttempts - 1; i++)
                toBump.MarkProcessing(RequestedAt.AddMinutes(i + 1));
            await seedDb.SaveChangesAsync();
        }

        await using (var checkDb = fixture.BuildContext())
        {
            var beforeFinal = await checkDb.AccountDisablements.SingleAsync(d => d.Id == request.Id);
            Assert.Equal(AccountDisablementJob.MaxAttempts - 1, beforeFinal.AttemptCount);
        }

        await using var db = fixture.BuildContext();
        var auditPublisher = new FakeAuditEventPublisher();
        var throwingClock = new SelectiveThrowingClock(Now.UtcDateTime, throwOnCallNumber: 2);
        var job = new AccountDisablementJob(db, throwingClock, auditPublisher, NullLogger<AccountDisablementJob>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ProcessAsync(request.Id, companyId));

        var reloaded = await db.AccountDisablements.SingleAsync(d => d.Id == request.Id);
        Assert.Equal(AccountDisablementJob.MaxAttempts, reloaded.AttemptCount);
        Assert.Equal(AccountDisablement.StatusFailed, reloaded.Status);
        Assert.Equal("Account disablement failed.", reloaded.FailureReason);
        Assert.NotNull(reloaded.LastAttemptAt);

        Assert.Single(
            auditPublisher.PublishedEvents, e => e.GetType().Name == "UserAccountDisablementFailedAuditEvent");
    }
}
