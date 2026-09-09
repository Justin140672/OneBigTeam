using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Persistence;
using HR.Modules.Reporting.Services;
using HR.Modules.Reporting.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Tests;

public class OrganisationDataExportJobStoreTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private static ReportingDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ReportingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static OrganisationDataExportJobStore StoreAt(ReportingDbContext db, DateTime now) =>
        new(db, new FakeClock(now));

    [Fact]
    public async Task Lifecycle_Marks_InProgress_Then_Completed()
    {
        await using var db = BuildContext();
        var export = OrganisationDataExport.Create(Guid.NewGuid(), Guid.NewGuid(), "Admin", new DateTimeOffset(Now));
        db.OrganisationDataExports.Add(export);
        await db.SaveChangesAsync();

        var store = StoreAt(db, Now);
        var token = Guid.NewGuid();

        Assert.True(await store.BeginAttemptAsync(export.Id, token, CancellationToken.None));
        Assert.True(await store.MarkCompletedAsync(export.Id, token, "organisation-exports/x/y.zip", 99, CancellationToken.None));

        var view = await store.GetAsync(export.Id, CancellationToken.None);
        Assert.NotNull(view);
        Assert.Equal("Completed", view!.Status);
        Assert.Equal("organisation-exports/x/y.zip", view.StorageKey);
        Assert.Null(view.LeaseOwnerToken);
    }

    [Fact]
    public async Task GetExpired_Returns_Only_Completed_Past_Expiry()
    {
        await using var db = BuildContext();
        var store = StoreAt(db, Now);

        var freshToken = Guid.NewGuid();
        var fresh = OrganisationDataExport.Create(Guid.NewGuid(), null, null, new DateTimeOffset(Now.AddDays(-1)));
        fresh.BeginAttempt(freshToken, new DateTimeOffset(Now.AddDays(-1)));
        fresh.MarkCompleted(freshToken, "k1", 1, new DateTimeOffset(Now.AddDays(-1))); // expires in 6 days

        var staleToken = Guid.NewGuid();
        var stale = OrganisationDataExport.Create(Guid.NewGuid(), null, null, new DateTimeOffset(Now.AddDays(-30)));
        stale.BeginAttempt(staleToken, new DateTimeOffset(Now.AddDays(-30)));
        stale.MarkCompleted(staleToken, "k2", 1, new DateTimeOffset(Now.AddDays(-30))); // expired 23 days ago

        db.OrganisationDataExports.AddRange(fresh, stale);
        await db.SaveChangesAsync();

        var expired = await store.GetExpiredAsync(CancellationToken.None);

        Assert.Single(expired);
        Assert.Equal(stale.Id, expired[0].Id);

        await store.MarkExpiredAsync(stale.Id, CancellationToken.None);
        var reloaded = await store.GetAsync(stale.Id, CancellationToken.None);
        Assert.Equal("Expired", reloaded!.Status);
    }

    [Fact]
    public async Task StatusReader_HasActiveExport_True_For_Pending_Or_InProgress_Only()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var reader = new OrganisationDataExportStatusReader(db);

        Assert.False(await reader.HasActiveExportAsync(companyId, CancellationToken.None));

        var pending = OrganisationDataExport.Create(companyId, null, null, new DateTimeOffset(Now));
        db.OrganisationDataExports.Add(pending);
        await db.SaveChangesAsync();
        Assert.True(await reader.HasActiveExportAsync(companyId, CancellationToken.None));

        var token = Guid.NewGuid();
        pending.BeginAttempt(token, new DateTimeOffset(Now));
        pending.MarkCompleted(token, "k", 1, new DateTimeOffset(Now));
        await db.SaveChangesAsync();
        Assert.False(await reader.HasActiveExportAsync(companyId, CancellationToken.None));
    }

    // ----- Ticket 3 / Follow-up A -----

    private static OrganisationDataExport Seed(ReportingDbContext db, Guid companyId, DateTimeOffset requestedAt)
    {
        var export = OrganisationDataExport.Create(companyId, Guid.NewGuid(), "Admin", requestedAt);
        db.OrganisationDataExports.Add(export);
        return export;
    }

    [Fact]
    public async Task BeginAttemptAsync_Moves_Pending_To_InProgress_Takes_Lease_And_Returns_True()
    {
        await using var db = BuildContext();
        var export = Seed(db, Guid.NewGuid(), new DateTimeOffset(Now));
        await db.SaveChangesAsync();
        var store = StoreAt(db, Now);
        var token = Guid.NewGuid();

        var claimed = await store.BeginAttemptAsync(export.Id, token, CancellationToken.None);

        Assert.True(claimed);
        var view = await store.GetAsync(export.Id, CancellationToken.None);
        Assert.Equal("InProgress", view!.Status);
        Assert.Equal(1, view.AttemptCount);
        Assert.Equal(new DateTimeOffset(Now), view.LastAttemptAt);
        Assert.Equal(token, view.LeaseOwnerToken);
        Assert.Equal(new DateTimeOffset(Now).AddMinutes(OrganisationDataExport.LeaseDurationMinutes), view.LeaseExpiresAt);
    }

    [Fact]
    public async Task BeginAttemptAsync_Second_Worker_Is_Refused_While_The_First_Lease_Is_Live()
    {
        await using var db = BuildContext();
        var export = Seed(db, Guid.NewGuid(), new DateTimeOffset(Now));
        await db.SaveChangesAsync();
        var store = StoreAt(db, Now);

        Assert.True(await store.BeginAttemptAsync(export.Id, Guid.NewGuid(), CancellationToken.None));
        Assert.False(await store.BeginAttemptAsync(export.Id, Guid.NewGuid(), CancellationToken.None));

        var view = await store.GetAsync(export.Id, CancellationToken.None);
        Assert.Equal(1, view!.AttemptCount);
    }

    [Fact]
    public async Task BeginAttemptAsync_A_Fresh_InProgress_Row_With_A_Live_Lease_Cannot_Be_Claimed()
    {
        await using var db = BuildContext();
        var export = Seed(db, Guid.NewGuid(), new DateTimeOffset(Now));
        export.BeginAttempt(Guid.NewGuid(), new DateTimeOffset(Now));
        await db.SaveChangesAsync();
        var store = StoreAt(db, Now);

        Assert.False(await store.BeginAttemptAsync(export.Id, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task BeginAttemptAsync_A_Crashed_Worker_Whose_Lease_Expired_Is_Taken_Over_With_A_Fresh_Lease()
    {
        await using var db = BuildContext();
        var export = Seed(db, Guid.NewGuid(), new DateTimeOffset(Now.AddMinutes(-40)));
        export.BeginAttempt(Guid.NewGuid(), new DateTimeOffset(Now.AddMinutes(-30))); // lease expired 15m ago
        await db.SaveChangesAsync();
        var store = StoreAt(db, Now);
        var replacement = Guid.NewGuid();

        Assert.True(await store.BeginAttemptAsync(export.Id, replacement, CancellationToken.None));

        var view = await store.GetAsync(export.Id, CancellationToken.None);
        Assert.Equal(2, view!.AttemptCount);
        Assert.Equal(replacement, view.LeaseOwnerToken);
        Assert.Equal(new DateTimeOffset(Now).AddMinutes(OrganisationDataExport.LeaseDurationMinutes), view.LeaseExpiresAt);
    }

    [Fact]
    public async Task BeginAttemptAsync_At_Attempt_Limit_Returns_False_Even_With_Expired_Lease()
    {
        await using var db = BuildContext();
        var export = Seed(db, Guid.NewGuid(), new DateTimeOffset(Now.AddHours(-2)));
        for (var i = 0; i < OrganisationDataExport.MaxAttempts; i++)
        {
            export.BeginAttempt(Guid.NewGuid(), new DateTimeOffset(Now.AddMinutes(-90 + i)));
            if (i < OrganisationDataExport.MaxAttempts - 1)
                export.ResetForRetry(new DateTimeOffset(Now.AddMinutes(-90 + i)));
        }
        await db.SaveChangesAsync();
        var store = StoreAt(db, Now);

        Assert.False(await store.BeginAttemptAsync(export.Id, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task BeginAttemptAsync_Returns_False_For_Completed_Row()
    {
        await using var db = BuildContext();
        var export = Seed(db, Guid.NewGuid(), new DateTimeOffset(Now));
        var token = Guid.NewGuid();
        export.BeginAttempt(token, new DateTimeOffset(Now));
        export.MarkCompleted(token, "k", 1, new DateTimeOffset(Now));
        await db.SaveChangesAsync();
        var store = StoreAt(db, Now);

        Assert.False(await store.BeginAttemptAsync(export.Id, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task BeginAttemptAsync_Returns_False_For_Missing_Id()
    {
        await using var db = BuildContext();
        var store = StoreAt(db, Now);

        Assert.False(await store.BeginAttemptAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task RenewLeaseAsync_Current_Owner_Succeeds_And_Pushes_Out_Expiry()
    {
        await using var db = BuildContext();
        var export = Seed(db, Guid.NewGuid(), new DateTimeOffset(Now.AddMinutes(-5)));
        var token = Guid.NewGuid();
        export.BeginAttempt(token, new DateTimeOffset(Now.AddMinutes(-5)));
        await db.SaveChangesAsync();
        var store = StoreAt(db, Now);

        Assert.True(await store.RenewLeaseAsync(export.Id, token, CancellationToken.None));

        var view = await store.GetAsync(export.Id, CancellationToken.None);
        Assert.Equal(new DateTimeOffset(Now).AddMinutes(OrganisationDataExport.LeaseDurationMinutes), view!.LeaseExpiresAt);
    }

    [Fact]
    public async Task RenewLeaseAsync_Stale_Token_Returns_False()
    {
        await using var db = BuildContext();
        var export = Seed(db, Guid.NewGuid(), new DateTimeOffset(Now));
        export.BeginAttempt(Guid.NewGuid(), new DateTimeOffset(Now));
        await db.SaveChangesAsync();
        var store = StoreAt(db, Now);

        Assert.False(await store.RenewLeaseAsync(export.Id, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task RenewLeaseAsync_Returns_False_Once_Status_Left_InProgress()
    {
        await using var db = BuildContext();
        var export = Seed(db, Guid.NewGuid(), new DateTimeOffset(Now));
        var token = Guid.NewGuid();
        export.BeginAttempt(token, new DateTimeOffset(Now));
        export.MarkCompleted(token, "k", 1, new DateTimeOffset(Now));
        await db.SaveChangesAsync();
        var store = StoreAt(db, Now);

        Assert.False(await store.RenewLeaseAsync(export.Id, token, CancellationToken.None));
    }

    [Fact]
    public async Task MarkCompletedAsync_By_Superseded_Worker_Returns_False_And_Does_Not_Overwrite()
    {
        await using var db = BuildContext();
        var export = Seed(db, Guid.NewGuid(), new DateTimeOffset(Now.AddMinutes(-40)));
        var original = Guid.NewGuid();
        export.BeginAttempt(original, new DateTimeOffset(Now.AddMinutes(-30))); // expired
        await db.SaveChangesAsync();
        var store = StoreAt(db, Now);

        // replacement worker takes over
        var replacement = Guid.NewGuid();
        Assert.True(await store.BeginAttemptAsync(export.Id, replacement, CancellationToken.None));

        // original worker resumes and tries to finish
        Assert.False(await store.MarkCompletedAsync(export.Id, original, "stale-key", 123, CancellationToken.None));

        var view = await store.GetAsync(export.Id, CancellationToken.None);
        Assert.Equal("InProgress", view!.Status);
        Assert.Null(view.StorageKey);
        Assert.Equal(replacement, view.LeaseOwnerToken);
    }

    [Fact]
    public async Task MarkFailedDueToMissingDocumentsAsync_With_Stale_Token_Is_A_No_Op()
    {
        await using var db = BuildContext();
        var export = Seed(db, Guid.NewGuid(), new DateTimeOffset(Now));
        export.BeginAttempt(Guid.NewGuid(), new DateTimeOffset(Now));
        await db.SaveChangesAsync();
        var store = StoreAt(db, Now);

        await store.MarkFailedDueToMissingDocumentsAsync(export.Id, Guid.NewGuid(), 3, CancellationToken.None);

        var reloaded = await db.OrganisationDataExports.AsNoTracking().SingleAsync(e => e.Id == export.Id);
        Assert.Equal("InProgress", reloaded.Status);
        Assert.Equal(0, reloaded.MissingDocumentCount);
    }

    [Fact]
    public async Task MarkFailedDueToMissingDocumentsAsync_By_Owner_Persists_Failed_Transition()
    {
        await using var db = BuildContext();
        var export = Seed(db, Guid.NewGuid(), new DateTimeOffset(Now));
        var token = Guid.NewGuid();
        export.BeginAttempt(token, new DateTimeOffset(Now));
        await db.SaveChangesAsync();
        var store = StoreAt(db, Now);

        await store.MarkFailedDueToMissingDocumentsAsync(export.Id, token, 2, CancellationToken.None);

        var reloaded = await db.OrganisationDataExports.AsNoTracking().SingleAsync(e => e.Id == export.Id);
        Assert.Equal("Failed", reloaded.Status);
        Assert.Equal(2, reloaded.MissingDocumentCount);
        Assert.Null(reloaded.LeaseOwnerToken);
    }

    [Fact]
    public async Task ResetForRetryAsync_Persists_InProgress_To_Pending_And_Clears_Lease()
    {
        await using var db = BuildContext();
        var export = Seed(db, Guid.NewGuid(), new DateTimeOffset(Now));
        export.BeginAttempt(Guid.NewGuid(), new DateTimeOffset(Now));
        await db.SaveChangesAsync();
        var store = StoreAt(db, Now);

        await store.ResetForRetryAsync(export.Id, CancellationToken.None);

        var view = await store.GetAsync(export.Id, CancellationToken.None);
        Assert.Equal("Pending", view!.Status);
        Assert.Null(view.StartedAt);
        Assert.Null(view.LeaseOwnerToken);
        Assert.Null(view.LeaseExpiresAt);
    }

    [Fact]
    public async Task GetRecoverableAsync_Returns_Stale_Pending_And_Lease_Expired_InProgress_But_Nothing_Healthy()
    {
        await using var db = BuildContext();
        var now = new DateTimeOffset(Now);
        var pendingCutoff = now.AddMinutes(-5);
        var leaseExpiredAsOf = now;

        var stalePending = Seed(db, Guid.NewGuid(), now.AddMinutes(-10));
        var freshPending = Seed(db, Guid.NewGuid(), now.AddMinutes(-1));

        // crashed worker: claimed 40m ago, 15m lease long expired
        var crashedInProgress = Seed(db, Guid.NewGuid(), now.AddHours(-2));
        crashedInProgress.BeginAttempt(Guid.NewGuid(), now.AddMinutes(-40));

        // healthy long-running worker: attempt started 2h ago but lease was just renewed
        var healthyInProgress = Seed(db, Guid.NewGuid(), now.AddHours(-3));
        var healthyToken = Guid.NewGuid();
        healthyInProgress.BeginAttempt(healthyToken, now.AddHours(-2));
        healthyInProgress.RenewLease(healthyToken, now.AddMinutes(-1));

        var completed = Seed(db, Guid.NewGuid(), now.AddHours(-2));
        var completedToken = Guid.NewGuid();
        completed.BeginAttempt(completedToken, now.AddHours(-2));
        completed.MarkCompleted(completedToken, "k", 1, now.AddHours(-1));

        await db.SaveChangesAsync();
        var store = StoreAt(db, Now);

        var recoverable = await store.GetRecoverableAsync(pendingCutoff, leaseExpiredAsOf, CancellationToken.None);
        var ids = recoverable.Select(r => r.Id).ToHashSet();

        Assert.Contains(stalePending.Id, ids);
        Assert.Contains(crashedInProgress.Id, ids);
        Assert.DoesNotContain(freshPending.Id, ids);
        Assert.DoesNotContain(healthyInProgress.Id, ids);
        Assert.DoesNotContain(completed.Id, ids);
    }

    [Fact]
    public async Task GetRecoverableAsync_A_Renewed_Long_Running_Export_Cannot_Be_Claimed_By_Another_Worker()
    {
        await using var db = BuildContext();
        var now = new DateTimeOffset(Now);
        var export = Seed(db, Guid.NewGuid(), now.AddHours(-3));
        var owner = Guid.NewGuid();
        export.BeginAttempt(owner, now.AddMinutes(-40)); // original attempt > 30m ago
        export.RenewLease(owner, now.AddMinutes(-2));    // but heartbeated recently
        await db.SaveChangesAsync();
        var store = StoreAt(db, Now);

        var recoverable = await store.GetRecoverableAsync(now.AddMinutes(-5), now, CancellationToken.None);
        Assert.DoesNotContain(export.Id, recoverable.Select(r => r.Id));

        Assert.False(await store.BeginAttemptAsync(export.Id, Guid.NewGuid(), CancellationToken.None));
    }

    // ----- Follow-up H: recovery claim -----

    [Fact]
    public async Task ClaimForRecoveryAsync_Takes_The_Lease_From_A_Crashed_Worker()
    {
        await using var db = BuildContext();
        var export = Seed(db, Guid.NewGuid(), new DateTimeOffset(Now.AddHours(-2)));
        export.BeginAttempt(Guid.NewGuid(), new DateTimeOffset(Now.AddMinutes(-40))); // lease expired
        await db.SaveChangesAsync();
        var store = StoreAt(db, Now);
        var recovery = Guid.NewGuid();

        Assert.True(await store.ClaimForRecoveryAsync(export.Id, recovery, CancellationToken.None));

        var view = await store.GetAsync(export.Id, CancellationToken.None);
        Assert.Equal("InProgress", view!.Status);
        Assert.Equal(recovery, view.LeaseOwnerToken);
        Assert.Equal(new DateTimeOffset(Now).AddMinutes(OrganisationDataExport.LeaseDurationMinutes), view.LeaseExpiresAt);
    }

    [Fact]
    public async Task ClaimForRecoveryAsync_Is_Refused_While_The_Original_Lease_Is_Still_Live()
    {
        await using var db = BuildContext();
        var export = Seed(db, Guid.NewGuid(), new DateTimeOffset(Now));
        export.BeginAttempt(Guid.NewGuid(), new DateTimeOffset(Now)); // lease live for 15m
        await db.SaveChangesAsync();
        var store = StoreAt(db, Now);

        Assert.False(await store.ClaimForRecoveryAsync(export.Id, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ClaimForRecoveryAsync_Then_ResetForRetryAsync_With_Same_Token_Returns_To_Pending()
    {
        await using var db = BuildContext();
        var export = Seed(db, Guid.NewGuid(), new DateTimeOffset(Now.AddHours(-2)));
        export.BeginAttempt(Guid.NewGuid(), new DateTimeOffset(Now.AddMinutes(-40)));
        await db.SaveChangesAsync();
        var store = StoreAt(db, Now);
        var recovery = Guid.NewGuid();

        Assert.True(await store.ClaimForRecoveryAsync(export.Id, recovery, CancellationToken.None));
        Assert.False(await store.ResetForRetryAsync(export.Id, Guid.NewGuid(), CancellationToken.None)); // wrong token
        Assert.True(await store.ResetForRetryAsync(export.Id, recovery, CancellationToken.None));

        var view = await store.GetAsync(export.Id, CancellationToken.None);
        Assert.Equal("Pending", view!.Status);
        Assert.Null(view.LeaseOwnerToken);
    }

    // ----- Follow-up I: artefact cleanup candidates -----

    [Fact]
    public async Task GetArtefactCleanupCandidatesAsync_Returns_Only_Uncleaned_Terminal_Rows_Oldest_First_And_Bounded()
    {
        await using var db = BuildContext();
        var store = StoreAt(db, Now);

        var t = Guid.NewGuid();
        var failedOld = Seed(db, Guid.NewGuid(), new DateTimeOffset(Now.AddDays(-5)));
        failedOld.BeginAttempt(t, new DateTimeOffset(Now.AddDays(-5)));
        failedOld.MarkFailed("boom", new DateTimeOffset(Now.AddDays(-5)));

        var failedNew = Seed(db, Guid.NewGuid(), new DateTimeOffset(Now.AddDays(-1)));
        failedNew.BeginAttempt(Guid.NewGuid(), new DateTimeOffset(Now.AddDays(-1)));
        failedNew.MarkFailed("boom", new DateTimeOffset(Now.AddDays(-1)));

        var pending = Seed(db, Guid.NewGuid(), new DateTimeOffset(Now));

        var inProgress = Seed(db, Guid.NewGuid(), new DateTimeOffset(Now));
        inProgress.BeginAttempt(Guid.NewGuid(), new DateTimeOffset(Now));

        await db.SaveChangesAsync();

        var all = await store.GetArtefactCleanupCandidatesAsync(50, CancellationToken.None);
        Assert.Equal(new[] { failedOld.Id, failedNew.Id }, all.Select(c => c.Id).ToArray());
        Assert.DoesNotContain(pending.Id, all.Select(c => c.Id));
        Assert.DoesNotContain(inProgress.Id, all.Select(c => c.Id));

        var bounded = await store.GetArtefactCleanupCandidatesAsync(1, CancellationToken.None);
        Assert.Equal(new[] { failedOld.Id }, bounded.Select(c => c.Id).ToArray());
    }

    [Fact]
    public async Task MarkAttemptFilesCleanedAsync_Removes_The_Row_From_Candidates_And_Adds_It_To_RecentlyCleaned()
    {
        await using var db = BuildContext();
        var store = StoreAt(db, Now);
        var export = Seed(db, Guid.NewGuid(), new DateTimeOffset(Now.AddDays(-2)));
        export.BeginAttempt(Guid.NewGuid(), new DateTimeOffset(Now.AddDays(-2)));
        export.MarkFailed("boom", new DateTimeOffset(Now.AddDays(-2)));
        await db.SaveChangesAsync();

        await store.MarkAttemptFilesCleanedAsync(export.Id, CancellationToken.None);

        Assert.DoesNotContain(export.Id,
            (await store.GetArtefactCleanupCandidatesAsync(50, CancellationToken.None)).Select(c => c.Id));
        Assert.Contains(export.Id,
            (await store.GetRecentlyCleanedArtefactsAsync(50, CancellationToken.None)).Select(c => c.Id));
    }

    [Fact]
    public async Task GetRecentlyCleanedArtefactsAsync_Ignores_Rows_Cleaned_More_Than_Fourteen_Days_Ago()
    {
        await using var db = BuildContext();
        var export = Seed(db, Guid.NewGuid(), new DateTimeOffset(Now.AddDays(-40)));
        export.BeginAttempt(Guid.NewGuid(), new DateTimeOffset(Now.AddDays(-40)));
        export.MarkFailed("boom", new DateTimeOffset(Now.AddDays(-40)));
        await db.SaveChangesAsync();

        // clean it 20 days ago
        await StoreAt(db, Now.AddDays(-20)).MarkAttemptFilesCleanedAsync(export.Id, CancellationToken.None);

        var recent = await StoreAt(db, Now).GetRecentlyCleanedArtefactsAsync(50, CancellationToken.None);
        Assert.DoesNotContain(export.Id, recent.Select(c => c.Id));
    }

    [Fact]
    public async Task GetRecoverableAsync_Still_Returns_Pending_Rows_Older_Than_The_Cutoff()
    {
        await using var db = BuildContext();
        var now = new DateTimeOffset(Now);
        var neverQueued = Seed(db, Guid.NewGuid(), now.AddMinutes(-20));
        await db.SaveChangesAsync();
        var store = StoreAt(db, Now);

        var recoverable = await store.GetRecoverableAsync(now.AddMinutes(-5), now, CancellationToken.None);

        Assert.Contains(neverQueued.Id, recoverable.Select(r => r.Id));
    }

    // ----- Ticket 3J: cross-context claim / renew / terminal-write concurrency -----
    //
    // Each scenario uses TWO ReportingDbContext instances over one shared in-memory database (one per
    // actor) so the worker's terminal write really reloads a row another context has mutated, matching
    // production where the worker and the renewal loop run on separate DbContext scopes.

    private static (ReportingDbContext CtxA, ReportingDbContext CtxB) TwoContextsOverOneDb()
    {
        var dbName = Guid.NewGuid().ToString("N");
        DbContextOptions<ReportingDbContext> Options() =>
            new DbContextOptionsBuilder<ReportingDbContext>().UseInMemoryDatabase(dbName).Options;
        return (new ReportingDbContext(Options()), new ReportingDbContext(Options()));
    }

    private static async Task<Guid> SeedExportAsync(ReportingDbContext db, DateTimeOffset requestedAt)
    {
        var export = OrganisationDataExport.Create(Guid.NewGuid(), Guid.NewGuid(), "Admin", requestedAt);
        db.OrganisationDataExports.Add(export);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return export.Id;
    }

    [Fact]
    public async Task Claim_A_Renew_B_Complete_A_Succeeds_On_First_Attempt_With_Lease_Cleared()
    {
        var (ctxA, ctxB) = TwoContextsOverOneDb();
        await using var _ = ctxA;
        await using var __ = ctxB;
        var id = await SeedExportAsync(ctxA, new DateTimeOffset(Now));

        var storeA = StoreAt(ctxA, Now);
        var storeB = StoreAt(ctxB, Now);
        var token = Guid.NewGuid();

        Assert.True(await storeA.BeginAttemptAsync(id, token, CancellationToken.None));
        Assert.True(await storeB.RenewLeaseAsync(id, token, CancellationToken.None));
        Assert.True(await storeA.MarkCompletedAsync(id, token, "organisation-exports/c/e/a.zip", 512, CancellationToken.None));

        var view = await storeA.GetAsync(id, CancellationToken.None);
        Assert.Equal("Completed", view!.Status);
        Assert.Equal("organisation-exports/c/e/a.zip", view.StorageKey);
        Assert.Null(view.LeaseOwnerToken);
        Assert.Null(view.LeaseExpiresAt);
    }

    [Fact]
    public async Task Claim_A_Then_Several_Renews_B_Then_Complete_A_Still_Succeeds()
    {
        var (ctxA, ctxB) = TwoContextsOverOneDb();
        await using var _ = ctxA;
        await using var __ = ctxB;
        var id = await SeedExportAsync(ctxA, new DateTimeOffset(Now));

        var storeA = StoreAt(ctxA, Now);
        var token = Guid.NewGuid();
        Assert.True(await storeA.BeginAttemptAsync(id, token, CancellationToken.None));

        for (var i = 1; i <= 4; i++)
        {
            var storeB = StoreAt(ctxB, Now.AddMinutes(i));
            Assert.True(await storeB.RenewLeaseAsync(id, token, CancellationToken.None));
        }

        Assert.True(await storeA.MarkCompletedAsync(id, token, "organisation-exports/c/e/final.zip", 99, CancellationToken.None));

        var view = await storeA.GetAsync(id, CancellationToken.None);
        Assert.Equal("Completed", view!.Status);
        Assert.Equal("organisation-exports/c/e/final.zip", view.StorageKey);
        Assert.Null(view.LeaseOwnerToken);
    }

    [Fact]
    public async Task Renew_B_Then_Ordinary_Owned_Failure_A_Returns_True_And_Status_Failed()
    {
        var (ctxA, ctxB) = TwoContextsOverOneDb();
        await using var _ = ctxA;
        await using var __ = ctxB;
        var id = await SeedExportAsync(ctxA, new DateTimeOffset(Now));

        var storeA = StoreAt(ctxA, Now);
        var storeB = StoreAt(ctxB, Now.AddMinutes(1));
        var token = Guid.NewGuid();

        Assert.True(await storeA.BeginAttemptAsync(id, token, CancellationToken.None));
        Assert.True(await storeB.RenewLeaseAsync(id, token, CancellationToken.None));
        Assert.True(await storeA.MarkFailedAsync(id, token, "Export could not be generated.", CancellationToken.None));

        var view = await storeA.GetAsync(id, CancellationToken.None);
        Assert.Equal("Failed", view!.Status);
        Assert.Null(view.LeaseOwnerToken);
    }

    [Fact]
    public async Task Renew_B_Then_MissingDocuments_Failure_A_Returns_True_And_Records_Count()
    {
        var (ctxA, ctxB) = TwoContextsOverOneDb();
        await using var _ = ctxA;
        await using var __ = ctxB;
        var id = await SeedExportAsync(ctxA, new DateTimeOffset(Now));

        var storeA = StoreAt(ctxA, Now);
        var storeB = StoreAt(ctxB, Now.AddMinutes(1));
        var token = Guid.NewGuid();

        Assert.True(await storeA.BeginAttemptAsync(id, token, CancellationToken.None));
        Assert.True(await storeB.RenewLeaseAsync(id, token, CancellationToken.None));
        Assert.True(await storeA.MarkFailedDueToMissingDocumentsAsync(id, token, 3, CancellationToken.None));

        var reloaded = await ctxB.OrganisationDataExports.AsNoTracking().SingleAsync(e => e.Id == id);
        Assert.Equal("Failed", reloaded.Status);
        Assert.Equal(3, reloaded.MissingDocumentCount);
        Assert.Null(reloaded.LeaseOwnerToken);
    }

    [Fact]
    public async Task Recovery_Takeover_By_B_Then_Old_Worker_A_Completion_And_Failure_Are_No_Ops()
    {
        var (ctxA, ctxB) = TwoContextsOverOneDb();
        await using var _ = ctxA;
        await using var __ = ctxB;
        var id = await SeedExportAsync(ctxA, new DateTimeOffset(Now.AddMinutes(-40)));

        var oldToken = Guid.NewGuid();
        var storeAOld = StoreAt(ctxA, Now.AddMinutes(-40));
        Assert.True(await storeAOld.BeginAttemptAsync(id, oldToken, CancellationToken.None)); // lease expires Now-25

        var recoveryToken = Guid.NewGuid();
        var storeBRecovery = StoreAt(ctxB, Now);
        Assert.True(await storeBRecovery.ClaimForRecoveryAsync(id, recoveryToken, CancellationToken.None));

        var storeANow = StoreAt(ctxA, Now);
        Assert.False(await storeANow.MarkCompletedAsync(id, oldToken, "stale.zip", 1, CancellationToken.None));
        Assert.False(await storeANow.MarkFailedAsync(id, oldToken, "boom", CancellationToken.None));

        var view = await storeBRecovery.GetAsync(id, CancellationToken.None);
        Assert.Equal("InProgress", view!.Status);
        Assert.Equal(recoveryToken, view.LeaseOwnerToken);
        Assert.Null(view.StorageKey);
    }

    [Fact]
    public async Task Recovery_Reset_To_Pending_By_B_Then_Old_Worker_A_Completion_And_Failure_Are_No_Ops()
    {
        var (ctxA, ctxB) = TwoContextsOverOneDb();
        await using var _ = ctxA;
        await using var __ = ctxB;
        var id = await SeedExportAsync(ctxA, new DateTimeOffset(Now.AddMinutes(-40)));

        var oldToken = Guid.NewGuid();
        Assert.True(await StoreAt(ctxA, Now.AddMinutes(-40)).BeginAttemptAsync(id, oldToken, CancellationToken.None));

        var recoveryToken = Guid.NewGuid();
        var storeB = StoreAt(ctxB, Now);
        Assert.True(await storeB.ClaimForRecoveryAsync(id, recoveryToken, CancellationToken.None));
        Assert.True(await storeB.ResetForRetryAsync(id, recoveryToken, CancellationToken.None));

        // Once recovery has reset the row to Pending the superseded worker's completion is a hard no-op
        // (MarkCompleted is only valid from InProgress).
        var storeANow = StoreAt(ctxA, Now);
        Assert.False(await storeANow.MarkCompletedAsync(id, oldToken, "stale.zip", 1, CancellationToken.None));

        var view = await storeB.GetAsync(id, CancellationToken.None);
        Assert.Equal("Pending", view!.Status);
        Assert.Null(view.LeaseOwnerToken);
        Assert.Null(view.StorageKey);
    }

    // ----- Ticket 3K: durable artefact-cleanup / late-upload-recheck selection cursors -----

    private static OrganisationDataExport SeedFailed(ReportingDbContext db, DateTimeOffset failedAt, Guid? companyId = null)
    {
        var export = OrganisationDataExport.Create(companyId ?? Guid.NewGuid(), Guid.NewGuid(), "Admin", failedAt);
        var token = Guid.NewGuid();
        export.BeginAttempt(token, failedAt);
        export.MarkFailed("boom", failedAt);
        db.OrganisationDataExports.Add(export);
        return export;
    }

    [Fact]
    public async Task GetArtefactCleanupCandidates_Orders_Nulls_First_Then_CompletedAt_Then_Id()
    {
        await using var db = BuildContext();

        // Two never-deferred rows (null cursor) — must come first, oldest completion first.
        var nullOld = SeedFailed(db, new DateTimeOffset(Now.AddDays(-6)));
        var nullNew = SeedFailed(db, new DateTimeOffset(Now.AddDays(-2)));
        // One row that has already been deferred into the past — must come last.
        var deferred = SeedFailed(db, new DateTimeOffset(Now.AddDays(-10)));
        await db.SaveChangesAsync();

        await StoreAt(db, Now.AddHours(-3)).DeferArtefactCleanupAsync(deferred.Id, CancellationToken.None);

        var ordered = await StoreAt(db, Now).GetArtefactCleanupCandidatesAsync(50, CancellationToken.None);

        Assert.Equal(new[] { nullOld.Id, nullNew.Id, deferred.Id }, ordered.Select(c => c.Id).ToArray());
    }

    [Fact]
    public async Task GetArtefactCleanupCandidates_Uses_Id_As_The_Final_Tie_Breaker()
    {
        await using var db = BuildContext();
        var sameInstant = new DateTimeOffset(Now.AddDays(-3));
        var a = SeedFailed(db, sameInstant);
        var b = SeedFailed(db, sameInstant);
        var c = SeedFailed(db, sameInstant);
        await db.SaveChangesAsync();

        var ordered = await StoreAt(db, Now).GetArtefactCleanupCandidatesAsync(50, CancellationToken.None);

        Assert.Equal(
            new[] { a.Id, b.Id, c.Id }.OrderBy(x => x).ToArray(),
            ordered.Select(x => x.Id).ToArray());
    }

    [Fact]
    public async Task GetArtefactCleanupCandidates_Drops_A_Deferred_Row_Until_Its_Backoff_Elapses()
    {
        await using var db = BuildContext();
        var export = SeedFailed(db, new DateTimeOffset(Now.AddDays(-3)));
        await db.SaveChangesAsync();

        await StoreAt(db, Now).DeferArtefactCleanupAsync(export.Id, CancellationToken.None); // next attempt = Now + 15m

        var notYetDue = await StoreAt(db, Now.AddMinutes(10)).GetArtefactCleanupCandidatesAsync(50, CancellationToken.None);
        Assert.DoesNotContain(export.Id, notYetDue.Select(c => c.Id));

        var due = await StoreAt(db, Now.AddMinutes(20)).GetArtefactCleanupCandidatesAsync(50, CancellationToken.None);
        Assert.Contains(export.Id, due.Select(c => c.Id));
    }

    [Fact]
    public async Task GetArtefactCleanupCandidates_Excludes_Rows_Whose_Artefacts_Are_Already_Cleaned()
    {
        await using var db = BuildContext();
        var export = SeedFailed(db, new DateTimeOffset(Now.AddDays(-3)));
        await db.SaveChangesAsync();
        await StoreAt(db, Now).MarkAttemptFilesCleanedAsync(export.Id, CancellationToken.None);

        var candidates = await StoreAt(db, Now).GetArtefactCleanupCandidatesAsync(50, CancellationToken.None);

        Assert.DoesNotContain(export.Id, candidates.Select(c => c.Id));
    }

    [Fact]
    public async Task GetRecentlyCleanedArtefacts_In_Window_With_No_Cursor_Is_Selected_But_A_Future_Cursor_Is_Deferred()
    {
        await using var db = BuildContext();
        var inWindow = SeedFailed(db, new DateTimeOffset(Now.AddDays(-3)));
        var futureCursor = SeedFailed(db, new DateTimeOffset(Now.AddDays(-3)));
        await db.SaveChangesAsync();

        await StoreAt(db, Now.AddDays(-2)).MarkAttemptFilesCleanedAsync(inWindow.Id, CancellationToken.None);
        await StoreAt(db, Now.AddDays(-2)).MarkAttemptFilesCleanedAsync(futureCursor.Id, CancellationToken.None);
        // Push futureCursor's recheck cursor to tomorrow via a successful recheck.
        await StoreAt(db, Now).RecordLateUploadRecheckAsync(futureCursor.Id, succeeded: true, CancellationToken.None);

        var due = await StoreAt(db, Now).GetRecentlyCleanedArtefactsAsync(50, CancellationToken.None);

        Assert.Contains(inWindow.Id, due.Select(c => c.Id));
        Assert.DoesNotContain(futureCursor.Id, due.Select(c => c.Id));

        var laterDue = await StoreAt(db, Now.AddDays(2)).GetRecentlyCleanedArtefactsAsync(50, CancellationToken.None);
        Assert.Contains(futureCursor.Id, laterDue.Select(c => c.Id));
    }

    [Fact]
    public async Task GetRecentlyCleanedArtefacts_Keeps_A_Row_With_A_Failed_Recheck_Cursor_Even_After_The_Window_Closes()
    {
        await using var db = BuildContext();
        var export = SeedFailed(db, new DateTimeOffset(Now.AddDays(-40)));
        await db.SaveChangesAsync();

        // Cleaned 20 days ago — already outside the 14-day window.
        await StoreAt(db, Now.AddDays(-20)).MarkAttemptFilesCleanedAsync(export.Id, CancellationToken.None);

        // With no cursor it is not selected once past the window...
        var beforeFailure = await StoreAt(db, Now).GetRecentlyCleanedArtefactsAsync(50, CancellationToken.None);
        Assert.DoesNotContain(export.Id, beforeFailure.Select(c => c.Id));

        // ...but a failed recheck leaves a cursor set, which keeps it retryable past the window.
        await StoreAt(db, Now.AddDays(-19)).RecordLateUploadRecheckAsync(export.Id, succeeded: false, CancellationToken.None);

        var afterFailure = await StoreAt(db, Now).GetRecentlyCleanedArtefactsAsync(50, CancellationToken.None);
        Assert.Contains(export.Id, afterFailure.Select(c => c.Id));
    }
}
