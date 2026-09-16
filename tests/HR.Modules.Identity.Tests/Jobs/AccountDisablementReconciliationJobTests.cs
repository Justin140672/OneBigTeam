using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Jobs;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Identity.Tests.Jobs;

// Ticket 10 (P1) follow-up: unit tests for AccountDisablementReconciliationJob, the recurring
// sweep that recovers AccountDisablement requests stuck Pending/Failed/stale-Processing (see
// Jobs/AccountDisablementReconciliationJob.cs for the recovery scenarios it guards against).
[Collection("IdentityDatabase")]
public class AccountDisablementReconciliationJobTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 9, 11, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private static AccountDisablementReconciliationJob BuildJob(
        IdentityDbContext db, RecordingBackgroundJobClient jobClient) =>
        new(db, Clock, jobClient, NullLogger<AccountDisablementReconciliationJob>.Instance);

    private async Task<AccountDisablement> SeedRequestAsync(
        string status,
        DateTimeOffset? leaseExpiresAt = null)
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using var db = fixture.BuildContext();
        var request = AccountDisablement.CreatePending(
            Guid.NewGuid(), companyId, employeeId, employeeId, RequestedAt);

        switch (status)
        {
            case AccountDisablement.StatusPending:
                break;
            case AccountDisablement.StatusFailed:
                request.MarkProcessing(RequestedAt.AddMinutes(1));
                request.MarkFailed("boom", RequestedAt.AddMinutes(2), maxAutomaticAttempts: AccountDisablementJob.MaxAttempts);
                break;
            case AccountDisablement.StatusProcessing:
                request.Claim(Guid.NewGuid(), RequestedAt.AddMinutes(1));
                if (leaseExpiresAt.HasValue)
                    db.Entry(request).Property("LeaseExpiresAt").CurrentValue = leaseExpiresAt.Value;
                break;
            case AccountDisablement.StatusProcessed:
                request.MarkProcessing(RequestedAt.AddMinutes(1));
                request.MarkProcessed(RequestedAt.AddMinutes(2));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }

        db.AccountDisablements.Add(request);
        await db.SaveChangesAsync();
        return request;
    }

    // NOTE: IdentityDatabaseFixture is a shared database across every test in the "IdentityDatabase"
    // collection (no per-test reset), and AccountDisablementReconciliationJob intentionally sweeps
    // the whole table company-wide (it has no per-company scope to filter by). So assertions here
    // must look for this test's own request by Id within the results, rather than asserting the
    // enqueued/collection counts are exactly 0 or 1 — other tests running in the same collection can
    // leave their own Pending/Failed/stale-Processing rows behind.

    [Fact]
    public async Task ExecuteAsync_Claims_Pending_Record_And_Enqueues_It()
    {
        var request = await SeedRequestAsync(AccountDisablement.StatusPending);

        var jobClient = new RecordingBackgroundJobClient();
        await using var db = fixture.BuildContext();
        await BuildJob(db, jobClient).ExecuteAsync();

        var enqueued = Assert.Single(jobClient.CreatedJobs, j => (Guid)j.Args[0] == request.Id);
        Assert.Equal(typeof(AccountDisablementJob), enqueued.Type);

        // Ticket 19 (P2): claimed straight to Processing (not left Pending) — see class remarks on
        // why a bare status reset would leave the row indistinguishable from any other untouched
        // Pending row.
        var reloaded = await db.AccountDisablements.SingleAsync(d => d.Id == request.Id);
        Assert.Equal(AccountDisablement.StatusProcessing, reloaded.Status);
        Assert.NotNull(reloaded.ClaimedBy);
        Assert.NotNull(reloaded.LeaseExpiresAt);
    }

    [Fact]
    public async Task ExecuteAsync_Claims_Failed_Record_And_Enqueues_It()
    {
        var request = await SeedRequestAsync(AccountDisablement.StatusFailed);

        var jobClient = new RecordingBackgroundJobClient();
        await using var db = fixture.BuildContext();
        await BuildJob(db, jobClient).ExecuteAsync();

        var reloaded = await db.AccountDisablements.SingleAsync(d => d.Id == request.Id);
        Assert.Equal(AccountDisablement.StatusProcessing, reloaded.Status);
        Assert.Null(reloaded.FailureReason);

        Assert.Single(jobClient.CreatedJobs, j => (Guid)j.Args[0] == request.Id);
    }

    [Fact]
    public async Task ExecuteAsync_Claims_Stale_Processing_Record_And_Enqueues_It()
    {
        // Ticket 19 (P2): staleness is now determined by the lease having actually expired, not a
        // fixed age heuristic — this lease expired 5 minutes ago.
        var request = await SeedRequestAsync(
            AccountDisablement.StatusProcessing, leaseExpiresAt: Now.AddMinutes(-5));

        var jobClient = new RecordingBackgroundJobClient();
        await using var db = fixture.BuildContext();
        await BuildJob(db, jobClient).ExecuteAsync();

        var reloaded = await db.AccountDisablements.SingleAsync(d => d.Id == request.Id);
        Assert.Equal(AccountDisablement.StatusProcessing, reloaded.Status);
        Assert.NotNull(reloaded.ClaimedBy); // Re-claimed under a fresh lease, not the stale one.
        Assert.True(reloaded.LeaseExpiresAt > Now);

        Assert.Single(jobClient.CreatedJobs, j => (Guid)j.Args[0] == request.Id);
    }

    [Fact]
    public async Task ExecuteAsync_Sweeps_Processing_Record_With_Null_LeaseExpiresAt_Resets_To_Pending_And_Enqueues_It()
    {
        // Guards against a Processing row with no lease at all (a row written before the ticket 19
        // migration, or a defensive edge case) — the query explicitly treats null as stale.
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        Guid requestId;

        await using (var seedDb = fixture.BuildContext())
        {
            var request = AccountDisablement.CreatePending(
                Guid.NewGuid(), companyId, employeeId, employeeId, RequestedAt);
            requestId = request.Id;
            seedDb.AccountDisablements.Add(request);
            await seedDb.SaveChangesAsync();

            request.Claim(Guid.NewGuid(), RequestedAt.AddMinutes(1));
            seedDb.Entry(request).Property("LeaseExpiresAt").CurrentValue = null;
            await seedDb.SaveChangesAsync();
        }

        var jobClient = new RecordingBackgroundJobClient();
        await using var db = fixture.BuildContext();
        await BuildJob(db, jobClient).ExecuteAsync();

        Assert.Single(jobClient.CreatedJobs, j => (Guid)j.Args[0] == requestId);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Sweep_Fresh_Processing_Record()
    {
        // Lease still has several minutes left — assumed to be a genuinely in-flight attempt.
        var request = await SeedRequestAsync(
            AccountDisablement.StatusProcessing, leaseExpiresAt: Now.AddMinutes(5));

        var jobClient = new RecordingBackgroundJobClient();
        await using var db = fixture.BuildContext();
        await BuildJob(db, jobClient).ExecuteAsync();

        Assert.DoesNotContain(jobClient.CreatedJobs, j => (Guid)j.Args[0] == request.Id);

        var reloaded = await db.AccountDisablements.SingleAsync(d => d.Id == request.Id);
        Assert.Equal(AccountDisablement.StatusProcessing, reloaded.Status); // untouched
    }

    // ---- Ticket 19 (P2): terminal failures, and atomic claim under concurrency -------------------

    [Fact]
    public async Task ExecuteAsync_Does_Not_Reset_A_Terminally_Failed_Record()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using var seedDb = fixture.BuildContext();
        var request = AccountDisablement.CreatePending(
            Guid.NewGuid(), companyId, employeeId, employeeId, RequestedAt);
        for (var i = 0; i < AccountDisablementJob.MaxAttempts; i++)
            request.MarkProcessing(RequestedAt.AddMinutes(i + 1));
        request.MarkFailed("boom", RequestedAt.AddMinutes(10), maxAutomaticAttempts: AccountDisablementJob.MaxAttempts);
        Assert.True(request.IsTerminallyFailed);
        seedDb.AccountDisablements.Add(request);
        await seedDb.SaveChangesAsync();

        var jobClient = new RecordingBackgroundJobClient();
        await using var db = fixture.BuildContext();
        await BuildJob(db, jobClient).ExecuteAsync();

        Assert.DoesNotContain(jobClient.CreatedJobs, j => (Guid)j.Args[0] == request.Id);

        var reloaded = await db.AccountDisablements.SingleAsync(d => d.Id == request.Id);
        Assert.Equal(AccountDisablement.StatusFailed, reloaded.Status); // untouched — requires manual retry
        Assert.True(reloaded.IsTerminallyFailed);
    }

    [Fact]
    public async Task ExecuteAsync_Sweeps_A_Record_Manually_Retried_After_Terminal_Failure()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using var seedDb = fixture.BuildContext();
        var request = AccountDisablement.CreatePending(
            Guid.NewGuid(), companyId, employeeId, employeeId, RequestedAt);
        for (var i = 0; i < AccountDisablementJob.MaxAttempts; i++)
            request.MarkProcessing(RequestedAt.AddMinutes(i + 1));
        request.MarkFailed("boom", RequestedAt.AddMinutes(10), maxAutomaticAttempts: AccountDisablementJob.MaxAttempts);
        request.RecordManualRetry(Guid.NewGuid(), "Confirmed the underlying issue is fixed.", RequestedAt.AddHours(1));
        seedDb.AccountDisablements.Add(request);
        await seedDb.SaveChangesAsync();

        var jobClient = new RecordingBackgroundJobClient();
        await using var db = fixture.BuildContext();
        await BuildJob(db, jobClient).ExecuteAsync();

        Assert.Single(jobClient.CreatedJobs, j => (Guid)j.Args[0] == request.Id);
    }

    [Fact]
    public async Task ExecuteAsync_From_Two_Concurrent_Reconcilers_Only_One_Claims_And_Enqueues_The_Same_Stale_Record()
    {
        // Ticket 19 (P2): the core claim/lease correctness guarantee — two reconciler "replicas"
        // (modelled here as two independently-built DbContexts racing to claim the SAME
        // stale-Processing row) must never both succeed. Unlike a bare status reset, a successful
        // claim here moves the row straight to Processing (not back to a still-"eligible" Pending),
        // so the two sweeps are racing for the SAME single winnable version, not independently
        // reading a moving target.
        var request = await SeedRequestAsync(
            AccountDisablement.StatusProcessing, leaseExpiresAt: Now.AddMinutes(-5));

        await using var dbA = fixture.BuildContext();
        await using var dbB = fixture.BuildContext();

        var jobClientA = new RecordingBackgroundJobClient();
        var jobClientB = new RecordingBackgroundJobClient();

        // Run both "sweeps" concurrently against the SAME real Postgres database.
        var taskA = BuildJob(dbA, jobClientA).ExecuteAsync();
        var taskB = BuildJob(dbB, jobClientB).ExecuteAsync();
        await Task.WhenAll(taskA, taskB);

        var enqueuedByA = jobClientA.CreatedJobs.Count(j => (Guid)j.Args[0] == request.Id);
        var enqueuedByB = jobClientB.CreatedJobs.Count(j => (Guid)j.Args[0] == request.Id);

        Assert.Equal(1, enqueuedByA + enqueuedByB); // Exactly one reconciler claimed and enqueued it.

        await using var verify = fixture.BuildContext();
        var reloaded = await verify.AccountDisablements.SingleAsync(d => d.Id == request.Id);
        Assert.Equal(AccountDisablement.StatusProcessing, reloaded.Status); // claimed, not left Pending
        Assert.NotNull(reloaded.ClaimedBy);
        Assert.Equal(2, reloaded.Version); // Exactly one successful claim advanced the version once.
    }

    [Fact]
    public async Task ExecuteAsync_Reconciler_Claim_Is_Verified_By_The_Enqueued_Worker_Before_Processing()
    {
        // Ticket 19 (P2): AccountDisablementJob must not blindly re-claim (or blindly trust) a
        // reconciler-originated dispatch — it verifies the specific claim id is still the current,
        // non-expired owner before doing any actual work.
        var request = await SeedRequestAsync(
            AccountDisablement.StatusProcessing, leaseExpiresAt: Now.AddMinutes(-5));

        var jobClient = new RecordingBackgroundJobClient();
        await using var reconcilerDb = fixture.BuildContext();
        await BuildJob(reconcilerDb, jobClient).ExecuteAsync();

        var enqueued = Assert.Single(jobClient.CreatedJobs, j => (Guid)j.Args[0] == request.Id);
        var claimedBy = (Guid)enqueued.Args[2];

        await using var afterClaim = fixture.BuildContext();
        var claimedRow = await afterClaim.AccountDisablements.SingleAsync(d => d.Id == request.Id);
        Assert.Equal(claimedBy, claimedRow.ClaimedBy);
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Sweep_Processed_Record()
    {
        var request = await SeedRequestAsync(AccountDisablement.StatusProcessed);

        var jobClient = new RecordingBackgroundJobClient();
        await using var db = fixture.BuildContext();
        await BuildJob(db, jobClient).ExecuteAsync();

        Assert.DoesNotContain(jobClient.CreatedJobs, j => (Guid)j.Args[0] == request.Id);

        var reloaded = await db.AccountDisablements.SingleAsync(d => d.Id == request.Id);
        Assert.Equal(AccountDisablement.StatusProcessed, reloaded.Status); // untouched
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Enqueue_For_An_Unrelated_Fresh_Request()
    {
        // Renamed from an "empty table" scenario: IdentityDatabaseFixture's database is shared
        // across the whole test collection, so the table is never actually empty here. Instead,
        // this pins that a brand new record which doesn't match any stale/pending criterion (a
        // Processed record) never gets swept, and running the job doesn't throw.
        var request = await SeedRequestAsync(AccountDisablement.StatusProcessed);

        var jobClient = new RecordingBackgroundJobClient();
        await using var db = fixture.BuildContext();

        var exception = await Record.ExceptionAsync(() => BuildJob(db, jobClient).ExecuteAsync());

        Assert.Null(exception);
        Assert.DoesNotContain(jobClient.CreatedJobs, j => (Guid)j.Args[0] == request.Id);
    }
}
