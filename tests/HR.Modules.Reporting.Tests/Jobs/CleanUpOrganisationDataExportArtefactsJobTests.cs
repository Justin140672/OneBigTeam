using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Jobs;
using HR.Modules.Reporting.Persistence;
using HR.Modules.Reporting.Services;
using HR.Modules.Reporting.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Reporting.Tests.Jobs;

/// <summary>
/// Follow-up I: <see cref="CleanUpOrganisationDataExportArtefactsJob"/> sweeps orphan attempt archives
/// left by failed / superseded organisation data export builds, preserves the published archive, skips
/// companies under a legal hold, is retryable (a delete failure leaves the row unmarked), and re-checks
/// recently-cleaned exports for stragglers.
/// </summary>
public class CleanUpOrganisationDataExportArtefactsJobTests
{
    private static CleanUpOrganisationDataExportArtefactsJob Build(
        FakeJobStore store, FakeStorage storage, FakeLegalHoldReader legalHold) =>
        new(store, storage, legalHold, NullLogger<CleanUpOrganisationDataExportArtefactsJob>.Instance);

    private static OrganisationDataExportJobView Terminal(Guid companyId, Guid id, string status, string? storageKey = null) =>
        new(id, companyId, status, storageKey, DateTimeOffset.UtcNow.AddDays(-1));

    [Fact]
    public async Task Failed_Export_With_Orphan_Attempt_Key_Is_Deleted_And_Marked_Cleaned()
    {
        var companyId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var export = Terminal(companyId, id, "Failed");
        var store = new FakeJobStore { Candidates = { export } };
        var storage = new FakeStorage();
        storage.AttemptKeys[id] = [$"organisation-exports/{companyId}/{id}/attempt-1.zip"];

        await Build(store, storage, new FakeLegalHoldReader()).ExecuteAsync();

        Assert.Equal(new[] { $"organisation-exports/{companyId}/{id}/attempt-1.zip" }, storage.DeletedKeys);
        Assert.Equal(new[] { id }, store.MarkedCleaned);
    }

    [Fact]
    public async Task Upload_Succeeded_But_Completion_Save_Lost_Is_The_Same_Shape_And_Is_Cleaned()
    {
        // Storage accepted the upload; the response (or the completion write) was lost, so the row is
        // Failed with an orphan attempt archive and no published StorageKey.
        var companyId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var store = new FakeJobStore { Candidates = { Terminal(companyId, id, "Failed") } };
        var storage = new FakeStorage();
        storage.AttemptKeys[id] =
        [
            $"organisation-exports/{companyId}/{id}/a.zip",
            $"organisation-exports/{companyId}/{id}/b.zip",
        ];

        await Build(store, storage, new FakeLegalHoldReader()).ExecuteAsync();

        Assert.Equal(2, storage.DeletedKeys.Count);
        Assert.Equal(new[] { id }, store.MarkedCleaned);
    }

    [Fact]
    public async Task Published_Archive_Of_A_Completed_Export_Is_Never_Deleted()
    {
        var companyId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var published = $"organisation-exports/{companyId}/{id}/published.zip";
        var store = new FakeJobStore { Candidates = { Terminal(companyId, id, "Completed", published) } };
        var storage = new FakeStorage();
        storage.AttemptKeys[id] = [published, $"organisation-exports/{companyId}/{id}/orphan.zip"];

        await Build(store, storage, new FakeLegalHoldReader()).ExecuteAsync();

        Assert.Equal(new[] { $"organisation-exports/{companyId}/{id}/orphan.zip" }, storage.DeletedKeys);
        Assert.DoesNotContain(published, storage.DeletedKeys);
        Assert.Equal(new[] { id }, store.MarkedCleaned);
    }

    [Fact]
    public async Task Delete_Failure_Leaves_The_Row_Unmarked_Then_Succeeds_And_Marks_On_The_Next_Run()
    {
        var companyId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var key = $"organisation-exports/{companyId}/{id}/a.zip";
        var store = new FakeJobStore { Candidates = { Terminal(companyId, id, "Failed") } };
        var storage = new FakeStorage { ThrowForKeys = { key } };
        storage.AttemptKeys[id] = [key];

        await Build(store, storage, new FakeLegalHoldReader()).ExecuteAsync();
        Assert.Empty(store.MarkedCleaned);

        storage.ThrowForKeys.Clear();
        await Build(store, storage, new FakeLegalHoldReader()).ExecuteAsync();
        Assert.Equal(new[] { id }, store.MarkedCleaned);
    }

    [Fact]
    public async Task List_Failure_Leaves_The_Row_Unmarked()
    {
        var companyId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var store = new FakeJobStore { Candidates = { Terminal(companyId, id, "Failed") } };
        var storage = new FakeStorage { ThrowOnListFor = { id } };

        await Build(store, storage, new FakeLegalHoldReader()).ExecuteAsync();

        Assert.Empty(store.MarkedCleaned);
        Assert.Empty(storage.DeletedKeys);
    }

    [Fact]
    public async Task Company_Under_Legal_Hold_Is_Skipped_And_Not_Marked_Cleaned()
    {
        var companyId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var store = new FakeJobStore { Candidates = { Terminal(companyId, id, "Failed") } };
        var storage = new FakeStorage();
        storage.AttemptKeys[id] = [$"organisation-exports/{companyId}/{id}/a.zip"];

        await Build(store, storage, new FakeLegalHoldReader(companyId)).ExecuteAsync();

        Assert.Empty(storage.DeletedKeys);
        Assert.Empty(store.MarkedCleaned);
    }

    [Fact]
    public async Task No_Orphan_Keys_Still_Marks_The_Row_Cleaned_And_Is_Idempotent()
    {
        var companyId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var store = new FakeJobStore { Candidates = { Terminal(companyId, id, "Failed") } };
        var storage = new FakeStorage();

        await Build(store, storage, new FakeLegalHoldReader()).ExecuteAsync();
        await Build(store, storage, new FakeLegalHoldReader()).ExecuteAsync();

        Assert.Empty(storage.DeletedKeys);
        Assert.Equal(new[] { id, id }, store.MarkedCleaned);
    }

    [Fact]
    public async Task Recheck_Pass_Deletes_A_Straggler_Uploaded_After_An_Earlier_Sweep_Without_Re_Marking()
    {
        // A superseded worker finished uploading after this export was already cleaned.
        var companyId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var straggler = $"organisation-exports/{companyId}/{id}/late.zip";
        var store = new FakeJobStore { RecentlyCleaned = { Terminal(companyId, id, "Failed") } };
        var storage = new FakeStorage();
        storage.AttemptKeys[id] = [straggler];

        await Build(store, storage, new FakeLegalHoldReader()).ExecuteAsync();

        Assert.Equal(new[] { straggler }, storage.DeletedKeys);
        Assert.Empty(store.MarkedCleaned); // recheck pass never re-marks
    }

    [Fact]
    public async Task Recheck_Pass_Also_Skips_Legally_Held_Companies()
    {
        var companyId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var store = new FakeJobStore { RecentlyCleaned = { Terminal(companyId, id, "Failed") } };
        var storage = new FakeStorage();
        storage.AttemptKeys[id] = [$"organisation-exports/{companyId}/{id}/late.zip"];

        await Build(store, storage, new FakeLegalHoldReader(companyId)).ExecuteAsync();

        Assert.Empty(storage.DeletedKeys);
    }

    [Fact]
    public async Task Both_Passes_Request_A_Bounded_Batch()
    {
        var store = new FakeJobStore();
        await Build(store, new FakeStorage(), new FakeLegalHoldReader()).ExecuteAsync();

        Assert.Equal(CleanUpOrganisationDataExportArtefactsJob.BatchSize, store.CandidateBatchSize);
        Assert.Equal(CleanUpOrganisationDataExportArtefactsJob.BatchSize, store.RecentBatchSize);
    }

    [Fact]
    public async Task Empty_Batches_Do_Nothing()
    {
        var store = new FakeJobStore();
        var storage = new FakeStorage();

        await Build(store, storage, new FakeLegalHoldReader()).ExecuteAsync();

        Assert.Empty(storage.DeletedKeys);
        Assert.Empty(store.MarkedCleaned);
    }

    // ======================================================================================
    // Ticket 3K — durable fairness of the cleanup sweep, exercised end to end against the REAL
    // OrganisationDataExportJobStore + REAL ReportingDbContext (EF InMemory) + REAL job. Each test
    // runs the job MULTIPLE times and asserts progress is made across runs — never that a batch size
    // was passed to a mock.
    // ======================================================================================

    private sealed class RealHarness
    {
        private readonly string _dbName = Guid.NewGuid().ToString("N");

        public FakeStorage Storage { get; } = new();
        public List<Guid> HeldCompanies { get; } = [];
        public DateTime Now { get; set; } = new(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);

        private ReportingDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ReportingDbContext>().UseInMemoryDatabase(_dbName).Options);

        public async Task SeedAsync(params OrganisationDataExport[] exports)
        {
            await using var db = NewContext();
            db.OrganisationDataExports.AddRange(exports);
            await db.SaveChangesAsync();
        }

        public async Task<OrganisationDataExport> ReloadAsync(Guid id)
        {
            await using var db = NewContext();
            return await db.OrganisationDataExports.AsNoTracking().SingleAsync(e => e.Id == id);
        }

        public async Task<IReadOnlyList<OrganisationDataExport>> AllAsync()
        {
            await using var db = NewContext();
            return await db.OrganisationDataExports.AsNoTracking().ToListAsync();
        }

        public async Task<IReadOnlyList<Guid>> CandidateIdsAsync()
        {
            await using var db = NewContext();
            var store = new OrganisationDataExportJobStore(db, new FakeClock(Now));
            var rows = await store.GetArtefactCleanupCandidatesAsync(
                CleanUpOrganisationDataExportArtefactsJob.BatchSize, CancellationToken.None);
            return rows.Select(r => r.Id).ToList();
        }

        // A "restart" is inherent: every run builds a fresh DbContext + store + job over the shared root.
        public async Task RunCleanupAsync()
        {
            await using var db = NewContext();
            var job = new CleanUpOrganisationDataExportArtefactsJob(
                new OrganisationDataExportJobStore(db, new FakeClock(Now)),
                Storage,
                new FakeLegalHoldReader([.. HeldCompanies]),
                NullLogger<CleanUpOrganisationDataExportArtefactsJob>.Instance);
            await job.ExecuteAsync();
        }

        public async Task RunPurgeExpiredAsync()
        {
            await using var db = NewContext();
            var job = new PurgeExpiredOrganisationDataExportsJob(
                new OrganisationDataExportJobStore(db, new FakeClock(Now)),
                Storage,
                new FakeLegalHoldReader([.. HeldCompanies]),
                NullLogger<PurgeExpiredOrganisationDataExportsJob>.Instance);
            await job.ExecuteAsync();
        }
    }

    private static OrganisationDataExport NewTerminal(
        Guid companyId, string status, DateTime whenUtc, string? publishedKey = null)
    {
        var at = new DateTimeOffset(whenUtc, TimeSpan.Zero);
        var export = OrganisationDataExport.Create(companyId, Guid.NewGuid(), "Admin", at);
        var token = Guid.NewGuid();
        export.BeginAttempt(token, at);
        switch (status)
        {
            case "Failed":
                export.MarkFailed("boom", at);
                break;
            case "Completed":
                export.MarkCompleted(token, publishedKey ?? "published.zip", 1, at);
                break;
            case "Expired":
                export.MarkCompleted(token, publishedKey ?? "published.zip", 1, at);
                export.MarkExpired(at.AddDays(30));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }

        return export;
    }

    private static string Orphan(OrganisationDataExport e, string name) =>
        $"organisation-exports/{e.CompanyId}/{e.Id}/{name}";

    // ----- Scenario 1 -----
    [Fact]
    public async Task Legal_Holds_On_The_Earliest_Fifty_Do_Not_Starve_Later_Exports()
    {
        var h = new RealHarness();
        var held = new List<OrganisationDataExport>();
        for (var i = 0; i < 50; i++)
        {
            var e = NewTerminal(Guid.NewGuid(), "Failed", h.Now.AddDays(-30).AddMinutes(i));
            h.Storage.AttemptKeys[e.Id] = [Orphan(e, "a.zip")];
            h.HeldCompanies.Add(e.CompanyId);
            held.Add(e);
        }

        var later = new List<OrganisationDataExport>();
        for (var i = 0; i < 5; i++)
        {
            var e = NewTerminal(Guid.NewGuid(), "Failed", h.Now.AddDays(-1).AddMinutes(i));
            h.Storage.AttemptKeys[e.Id] = [Orphan(e, "a.zip")];
            later.Add(e);
        }

        await h.SeedAsync([.. held, .. later]);

        await h.RunCleanupAsync();          // batch = oldest 50, all held -> all deferred
        h.Now = h.Now.AddMinutes(2);
        await h.RunCleanupAsync();          // held rows now behind a backoff -> later 5 take the slots

        var all = (await h.AllAsync()).ToDictionary(e => e.Id);
        Assert.All(later, e => Assert.NotNull(all[e.Id].AttemptFilesCleanedAt));
        Assert.All(held, e => Assert.Null(all[e.Id].AttemptFilesCleanedAt));
    }

    // ----- Scenario 2 -----
    [Fact]
    public async Task Persistent_Delete_Failures_On_The_Earliest_Do_Not_Starve_Later_Exports()
    {
        var h = new RealHarness();
        var broken = new List<OrganisationDataExport>();
        for (var i = 0; i < 50; i++)
        {
            var e = NewTerminal(Guid.NewGuid(), "Failed", h.Now.AddDays(-30).AddMinutes(i));
            var key = Orphan(e, "a.zip");
            h.Storage.AttemptKeys[e.Id] = [key];
            h.Storage.ThrowForKeys.Add(key);
            broken.Add(e);
        }

        var later = new List<OrganisationDataExport>();
        for (var i = 0; i < 5; i++)
        {
            var e = NewTerminal(Guid.NewGuid(), "Failed", h.Now.AddDays(-1).AddMinutes(i));
            h.Storage.AttemptKeys[e.Id] = [Orphan(e, "a.zip")];
            later.Add(e);
        }

        await h.SeedAsync([.. broken, .. later]);

        await h.RunCleanupAsync();
        h.Now = h.Now.AddMinutes(2);
        await h.RunCleanupAsync();

        var all = (await h.AllAsync()).ToDictionary(e => e.Id);
        Assert.All(later, e => Assert.NotNull(all[e.Id].AttemptFilesCleanedAt));
        Assert.All(broken, e => Assert.Null(all[e.Id].AttemptFilesCleanedAt));
        Assert.All(broken, e => Assert.True(all[e.Id].ArtefactCleanupAttemptCount >= 1));
    }

    // ----- Scenario 3 -----
    [Fact]
    public async Task A_Late_Upload_On_An_Older_Cleaned_Entry_Is_Discovered_Across_Repeated_Runs()
    {
        var h = new RealHarness();
        var rows = new List<OrganisationDataExport>();
        for (var i = 0; i < 55; i++)
        {
            var e = NewTerminal(Guid.NewGuid(), "Failed", h.Now.AddDays(-20).AddMinutes(i));
            e.MarkAttemptFilesCleaned(new DateTimeOffset(h.Now.AddDays(-2).AddSeconds(i), TimeSpan.Zero));
            rows.Add(e);
        }

        // The straggler lands on an entry that is NOT in the first batch of 50 (52nd by cleaned time).
        var straggler = rows[52];
        var lateKey = Orphan(straggler, "late.zip");
        h.Storage.AddAttemptKeys(straggler.Id, lateKey);

        await h.SeedAsync([.. rows]);

        await h.RunCleanupAsync();
        Assert.DoesNotContain(lateKey, h.Storage.DeletedKeys); // first batch didn't reach it

        h.Now = h.Now.AddMinutes(1);
        await h.RunCleanupAsync();
        Assert.Contains(lateKey, h.Storage.DeletedKeys);       // second run rotates onto it
    }

    // ----- Scenario 4 -----
    [Fact]
    public async Task Equal_Scheduling_Timestamps_Still_Let_Every_Entry_Progress_Via_The_Id_Tie_Breaker()
    {
        var h = new RealHarness();
        var when = h.Now.AddDays(-3);
        var rows = new List<OrganisationDataExport>();
        for (var i = 0; i < 55; i++)
        {
            var e = NewTerminal(Guid.NewGuid(), "Failed", when); // identical CompletedAt
            h.Storage.AttemptKeys[e.Id] = [Orphan(e, "a.zip")];
            rows.Add(e);
        }

        await h.SeedAsync([.. rows]);

        await h.RunCleanupAsync();
        h.Now = h.Now.AddMinutes(1);
        await h.RunCleanupAsync();

        var all = (await h.AllAsync()).ToDictionary(e => e.Id);
        Assert.All(rows, e => Assert.NotNull(all[e.Id].AttemptFilesCleanedAt));
    }

    // ----- Scenario 5 -----
    [Fact]
    public async Task A_Cleanup_Failure_Then_A_Job_Restart_Eventually_Succeeds()
    {
        var h = new RealHarness();
        var e = NewTerminal(Guid.NewGuid(), "Failed", h.Now.AddDays(-3));
        var key = Orphan(e, "a.zip");
        h.Storage.AttemptKeys[e.Id] = [key];
        h.Storage.ThrowForKeys.Add(key);
        await h.SeedAsync(e);

        await h.RunCleanupAsync(); // fails -> deferred
        var afterFailure = await h.ReloadAsync(e.Id);
        Assert.Null(afterFailure.AttemptFilesCleanedAt);
        Assert.NotNull(afterFailure.ArtefactCleanupNextAttemptAt);
        Assert.Equal(1, afterFailure.ArtefactCleanupAttemptCount);

        h.Storage.ThrowForKeys.Clear();
        h.Now = h.Now.AddMinutes(20); // past the backoff

        await h.RunCleanupAsync(); // brand new DbContext + store + job over the same root
        var afterRestart = await h.ReloadAsync(e.Id);
        Assert.NotNull(afterRestart.AttemptFilesCleanedAt);
        Assert.Null(afterRestart.ArtefactCleanupNextAttemptAt);
        Assert.Contains(key, h.Storage.DeletedKeys);
    }

    // ----- Scenario 6 -----
    [Fact]
    public async Task A_Failed_Late_Upload_Recheck_Near_The_Window_Edge_Stays_Retryable_Past_Fourteen_Days()
    {
        var h = new RealHarness();
        var cleanedAt = h.Now.AddDays(-13);
        var e = NewTerminal(Guid.NewGuid(), "Failed", h.Now.AddDays(-20));
        e.MarkAttemptFilesCleaned(new DateTimeOffset(cleanedAt, TimeSpan.Zero));
        var lateKey = Orphan(e, "late.zip");
        h.Storage.AttemptKeys[e.Id] = [lateKey];
        h.Storage.ThrowForKeys.Add(lateKey); // recheck delete fails
        await h.SeedAsync(e);

        await h.RunCleanupAsync(); // recheck fails -> cursor set, still inside the window
        var afterFail = await h.ReloadAsync(e.Id);
        Assert.NotNull(afterFail.LateUploadRecheckNextAt);
        Assert.Equal(1, afterFail.LateUploadRecheckAttemptCount);

        // Move well past the 14-day window and let the delete succeed this time.
        h.Now = h.Now.AddDays(3); // cleanedAt is now 16 days old
        h.Storage.ThrowForKeys.Clear();

        await h.RunCleanupAsync(); // row is still selected because its cursor is non-null
        var afterResolve = await h.ReloadAsync(e.Id);
        Assert.Contains(lateKey, h.Storage.DeletedKeys);
        Assert.Null(afterResolve.LateUploadRecheckNextAt); // success outside window stops the recheck
    }

    // ----- Scenario 7 -----
    [Fact]
    public async Task A_Legal_Hold_Removed_Makes_A_Previously_Skipped_Export_Processable_On_A_Later_Run()
    {
        var h = new RealHarness();
        var e = NewTerminal(Guid.NewGuid(), "Failed", h.Now.AddDays(-3));
        h.Storage.AttemptKeys[e.Id] = [Orphan(e, "a.zip")];
        h.HeldCompanies.Add(e.CompanyId);
        await h.SeedAsync(e);

        await h.RunCleanupAsync();
        Assert.Null((await h.ReloadAsync(e.Id)).AttemptFilesCleanedAt);

        h.HeldCompanies.Clear();
        h.Now = h.Now.AddMinutes(20);

        await h.RunCleanupAsync();
        Assert.NotNull((await h.ReloadAsync(e.Id)).AttemptFilesCleanedAt);
        Assert.Contains(Orphan(e, "a.zip"), h.Storage.DeletedKeys);
    }

    // ----- Scenario 8 -----
    [Fact]
    public async Task A_Previously_Cleaned_Completed_Export_That_Expires_Has_Its_Remaining_Files_Removed()
    {
        var h = new RealHarness();
        var publishedKey = "organisation-exports/x/published.zip";
        var e = NewTerminal(Guid.NewGuid(), "Completed", h.Now.AddDays(-10), publishedKey); // expires Now-3d
        e.MarkAttemptFilesCleaned(new DateTimeOffset(h.Now.AddDays(-9), TimeSpan.Zero));
        var leftover = Orphan(e, "leftover.zip");
        h.Storage.AttemptKeys[e.Id] = [publishedKey, leftover];
        await h.SeedAsync(e);

        // The artefact-cleanup cursor won't re-pick an already-cleaned row; the expiry purge re-lists
        // and removes everything that is still in storage.
        await h.RunPurgeExpiredAsync();

        var reloaded = await h.ReloadAsync(e.Id);
        Assert.Equal(OrganisationDataExport.StatusExpired, reloaded.Status);
        Assert.Contains(leftover, h.Storage.DeletedKeys);
        Assert.Contains(publishedKey, h.Storage.DeletedKeys);
    }

    // ----- Scenario 9 -----
    [Fact]
    public async Task Active_Exports_And_Retained_Published_Archives_Are_Never_Touched()
    {
        var h = new RealHarness();

        var pending = OrganisationDataExport.Create(Guid.NewGuid(), Guid.NewGuid(), "Admin", new DateTimeOffset(h.Now, TimeSpan.Zero));

        var inProgress = OrganisationDataExport.Create(Guid.NewGuid(), Guid.NewGuid(), "Admin", new DateTimeOffset(h.Now, TimeSpan.Zero));
        inProgress.BeginAttempt(Guid.NewGuid(), new DateTimeOffset(h.Now, TimeSpan.Zero));

        var publishedKey = "organisation-exports/pub/keep.zip";
        var completed = NewTerminal(Guid.NewGuid(), "Completed", h.Now.AddDays(-1), publishedKey);
        var orphan = Orphan(completed, "orphan.zip");
        h.Storage.AttemptKeys[completed.Id] = [publishedKey, orphan];

        await h.SeedAsync(pending, inProgress, completed);

        var candidateIds = await h.CandidateIdsAsync();
        Assert.DoesNotContain(pending.Id, candidateIds);
        Assert.DoesNotContain(inProgress.Id, candidateIds);
        Assert.Contains(completed.Id, candidateIds);

        await h.RunCleanupAsync();
        await h.RunCleanupAsync();

        var all = (await h.AllAsync()).ToDictionary(e => e.Id);
        Assert.Equal(OrganisationDataExport.StatusPending, all[pending.Id].Status);
        Assert.Equal(OrganisationDataExport.StatusInProgress, all[inProgress.Id].Status);
        Assert.Null(all[pending.Id].AttemptFilesCleanedAt);
        Assert.Null(all[inProgress.Id].AttemptFilesCleanedAt);

        Assert.NotNull(all[completed.Id].AttemptFilesCleanedAt);
        Assert.Contains(orphan, h.Storage.DeletedKeys);
        Assert.DoesNotContain(publishedKey, h.Storage.DeletedKeys);
    }

    private sealed class FakeJobStore : IOrganisationDataExportJobStore
    {
        public List<OrganisationDataExportJobView> Candidates { get; } = [];
        public List<OrganisationDataExportJobView> RecentlyCleaned { get; } = [];
        public List<Guid> MarkedCleaned { get; } = [];
        public List<Guid> Deferred { get; } = [];
        public List<(Guid Id, bool Succeeded)> Rechecks { get; } = [];
        public int? CandidateBatchSize { get; private set; }
        public int? RecentBatchSize { get; private set; }

        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetArtefactCleanupCandidatesAsync(int batchSize, CancellationToken cancellationToken)
        {
            CandidateBatchSize = batchSize;
            return Task.FromResult<IReadOnlyList<OrganisationDataExportJobView>>(Candidates.Take(batchSize).ToList());
        }

        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetRecentlyCleanedArtefactsAsync(int batchSize, CancellationToken cancellationToken)
        {
            RecentBatchSize = batchSize;
            return Task.FromResult<IReadOnlyList<OrganisationDataExportJobView>>(RecentlyCleaned.Take(batchSize).ToList());
        }

        public Task MarkAttemptFilesCleanedAsync(Guid exportId, CancellationToken cancellationToken)
        {
            MarkedCleaned.Add(exportId);
            return Task.CompletedTask;
        }

        public Task DeferArtefactCleanupAsync(Guid exportId, CancellationToken cancellationToken)
        {
            Deferred.Add(exportId);
            return Task.CompletedTask;
        }

        public Task RecordLateUploadRecheckAsync(Guid exportId, bool succeeded, CancellationToken cancellationToken)
        {
            Rechecks.Add((exportId, succeeded));
            return Task.CompletedTask;
        }

        public Task<OrganisationDataExportJobView?> GetAsync(Guid exportId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkInProgressAsync(Guid exportId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> BeginAttemptAsync(Guid exportId, Guid ownerToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> RenewLeaseAsync(Guid exportId, Guid ownerToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> MarkCompletedAsync(Guid exportId, Guid ownerToken, string storageKey, long fileSizeBytes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkFailedAsync(Guid exportId, string failureReason, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> MarkFailedAsync(Guid exportId, Guid ownerToken, string failureReason, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> MarkFailedDueToMissingDocumentsAsync(Guid exportId, Guid ownerToken, int missingCount, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ResetForRetryAsync(Guid exportId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ClaimForRecoveryAsync(Guid exportId, Guid recoveryToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ResetForRetryAsync(Guid exportId, Guid recoveryToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetRecoverableAsync(DateTimeOffset pendingQueuedBefore, DateTimeOffset leaseExpiredAsOf, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetExpiredAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkExpiredAsync(Guid exportId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeStorage : IOrganisationDataExportStorage
    {
        public Dictionary<Guid, List<string>> AttemptKeys { get; } = [];
        public List<string> DeletedKeys { get; } = [];
        public HashSet<string> ThrowForKeys { get; } = [];
        public HashSet<Guid> ThrowOnListFor { get; } = [];

        /// <summary>Ticket 3K: simulate a superseded worker finishing an upload after an earlier sweep.</summary>
        public void AddAttemptKeys(Guid exportId, params string[] keys)
        {
            if (!AttemptKeys.TryGetValue(exportId, out var list))
                AttemptKeys[exportId] = list = [];
            list.AddRange(keys);
        }

        public Task<IReadOnlyList<string>> ListAttemptKeysAsync(Guid companyId, Guid exportId, CancellationToken cancellationToken)
        {
            if (ThrowOnListFor.Contains(exportId))
                throw new InvalidOperationException("list boom");
            return Task.FromResult<IReadOnlyList<string>>(
                AttemptKeys.TryGetValue(exportId, out var keys) ? keys.ToList() : []);
        }

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
        {
            if (ThrowForKeys.Contains(storageKey))
                throw new InvalidOperationException("delete boom");
            DeletedKeys.Add(storageKey);
            foreach (var list in AttemptKeys.Values)
                list.Remove(storageKey);
            return Task.CompletedTask;
        }

        public Task<string> UploadAsync(Guid companyId, Guid exportId, Guid attemptToken, Stream content, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeLegalHoldReader(params Guid[] heldCompanyIds) : ILegalHoldStatusReader
    {
        private readonly HashSet<Guid> _held = [.. heldCompanyIds];

        public Task<bool> IsUnderLegalHoldAsync(Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult(_held.Contains(companyId));
    }
}
