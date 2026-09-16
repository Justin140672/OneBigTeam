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
        DateTimeOffset? lastAttemptAt = null)
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
                request.MarkFailed("boom", RequestedAt.AddMinutes(2));
                break;
            case AccountDisablement.StatusProcessing:
                request.MarkProcessing(lastAttemptAt ?? RequestedAt.AddMinutes(1));
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
    public async Task ExecuteAsync_Sweeps_Pending_Record_And_Enqueues_It()
    {
        var request = await SeedRequestAsync(AccountDisablement.StatusPending);

        var jobClient = new RecordingBackgroundJobClient();
        await using var db = fixture.BuildContext();
        await BuildJob(db, jobClient).ExecuteAsync();

        var enqueued = Assert.Single(jobClient.CreatedJobs, j => (Guid)j.Args[0] == request.Id);
        Assert.Equal(typeof(AccountDisablementJob), enqueued.Type);

        var reloaded = await db.AccountDisablements.SingleAsync(d => d.Id == request.Id);
        Assert.Equal(AccountDisablement.StatusPending, reloaded.Status);
    }

    [Fact]
    public async Task ExecuteAsync_Sweeps_Failed_Record_Resets_To_Pending_And_Enqueues_It()
    {
        var request = await SeedRequestAsync(AccountDisablement.StatusFailed);

        var jobClient = new RecordingBackgroundJobClient();
        await using var db = fixture.BuildContext();
        await BuildJob(db, jobClient).ExecuteAsync();

        var reloaded = await db.AccountDisablements.SingleAsync(d => d.Id == request.Id);
        Assert.Equal(AccountDisablement.StatusPending, reloaded.Status);
        Assert.Null(reloaded.FailureReason);

        Assert.Single(jobClient.CreatedJobs, j => (Guid)j.Args[0] == request.Id);
    }

    [Fact]
    public async Task ExecuteAsync_Sweeps_Stale_Processing_Record_Resets_To_Pending_And_Enqueues_It()
    {
        // Older than the 15-minute stale-processing threshold.
        var request = await SeedRequestAsync(
            AccountDisablement.StatusProcessing, lastAttemptAt: Now.AddMinutes(-20));

        var jobClient = new RecordingBackgroundJobClient();
        await using var db = fixture.BuildContext();
        await BuildJob(db, jobClient).ExecuteAsync();

        var reloaded = await db.AccountDisablements.SingleAsync(d => d.Id == request.Id);
        Assert.Equal(AccountDisablement.StatusPending, reloaded.Status);

        Assert.Single(jobClient.CreatedJobs, j => (Guid)j.Args[0] == request.Id);
    }

    [Fact]
    public async Task ExecuteAsync_Sweeps_Processing_Record_With_Null_LastAttemptAt_Resets_To_Pending_And_Enqueues_It()
    {
        // Guards against a Processing row that never even recorded a LastAttemptAt (shouldn't
        // normally happen since MarkProcessing always sets it, but the query explicitly treats
        // null as stale — this pins that behaviour).
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

            // Force a Processing status without a LastAttemptAt via direct EF update, since the
            // domain's own MarkProcessing always stamps one.
            request.MarkProcessing(RequestedAt.AddMinutes(1));
            seedDb.Entry(request).Property("LastAttemptAt").CurrentValue = null;
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
        // Within the last few minutes — assumed to be a genuinely in-flight attempt.
        var request = await SeedRequestAsync(
            AccountDisablement.StatusProcessing, lastAttemptAt: Now.AddMinutes(-2));

        var jobClient = new RecordingBackgroundJobClient();
        await using var db = fixture.BuildContext();
        await BuildJob(db, jobClient).ExecuteAsync();

        Assert.DoesNotContain(jobClient.CreatedJobs, j => (Guid)j.Args[0] == request.Id);

        var reloaded = await db.AccountDisablements.SingleAsync(d => d.Id == request.Id);
        Assert.Equal(AccountDisablement.StatusProcessing, reloaded.Status); // untouched
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
