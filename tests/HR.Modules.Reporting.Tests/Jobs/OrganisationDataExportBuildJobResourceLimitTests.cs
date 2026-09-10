using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using HR.Infrastructure.Abstractions;
using HR.Infrastructure.Storage;
using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Jobs;
using HR.Modules.Reporting.Persistence;
using HR.Modules.Reporting.Services;
using HR.Modules.Reporting.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Reporting.Tests.Jobs;

/// <summary>
/// Ticket 4: the resource-limit behaviour of the organisation data export build job — concurrency
/// slot acquisition, the bounded temp-disk workspace, streamed upload and workspace cleanup on every
/// exit path. Local, trimmed harness so the passing <see cref="OrganisationDataExportBuildJobTests"/>
/// stays untouched. Every test uses a unique in-memory database and a unique temp work root, holds no
/// shared static state, and relies on the existing "OpenDelay + Assert Renewer.CallCount &gt; 1"
/// pattern rather than fixed sleeps.
/// </summary>
public sealed class OrganisationDataExportBuildJobResourceLimitTests
{
    private static readonly DateTime Now = new(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan FastRenewInterval = TimeSpan.FromMilliseconds(20);

    private static ReportingDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ReportingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static byte[] RandomBytes(int count)
    {
        var b = new byte[count];
        RandomNumberGenerator.Fill(b);
        return b;
    }

    private sealed record Harness(
        OrganisationDataExportBuildJob Job,
        ReportingDbContext Db,
        IOrganisationDataExportJobStore Store,
        FakeManifest Manifest,
        IOrganisationDataExportStorage Storage,
        RecordingPublisher Publisher,
        CapturingAdministrativeAlertWriter Alerts,
        RenewerSpy Renewer,
        string WorkRoot)
    {
        public FakeStorage FakeStorage => (FakeStorage)Storage;

        public string[] WorkspaceDirs =>
            Directory.Exists(WorkRoot) ? Directory.GetDirectories(WorkRoot) : [];
    }

    private static Harness CreateHarness(
        out OrganisationDataExport export,
        IOrganisationDataExportStorage? storage = null,
        FakeManifest? manifest = null,
        IOrganisationDataExportConcurrencyGate? gate = null,
        OrganisationDataExportResourceLimits? limits = null,
        IAuditDataExportSource? auditSource = null)
    {
        var db = BuildContext();
        var company = Guid.NewGuid();
        export = OrganisationDataExport.Create(company, Guid.NewGuid(), "Admin", new DateTimeOffset(Now.AddMinutes(-10)));
        db.OrganisationDataExports.Add(export);
        db.SaveChanges();

        var clock = new FakeClock(Now);

        // Both the worker and the renewal loop share this single in-memory context in the harness, so
        // serialise every store call to model the production per-scope isolation and keep the
        // renewal-loop assertions deterministic at high parallelism.
        IOrganisationDataExportJobStore store = new SerializingJobStore(new OrganisationDataExportJobStore(db, clock));

        storage ??= new FakeStorage();
        manifest ??= new FakeManifest();
        var publisher = new RecordingPublisher();
        var alerts = new CapturingAdministrativeAlertWriter();
        var sources = new EmptySources();
        var renewer = new RenewerSpy(store);

        // MinimumFreeDiskBytes = 0: the CI host's real free space must never make a test flaky.
        limits ??= new OrganisationDataExportResourceLimits { MinimumFreeDiskBytes = 0 };
        var workRoot = Path.Combine(Path.GetTempPath(), "obt-test-buildjob-rl", Guid.NewGuid().ToString("N"));
        var workspaceFactory = new OrganisationDataExportWorkspaceFactory(limits, rootOverride: workRoot);

        var job = new OrganisationDataExportBuildJob(
            store, sources, sources, sources, sources, auditSource ?? sources, manifest,
            new OrganisationDataExportPackageBuilder(), storage,
            gate ?? new PassThroughGate(), workspaceFactory,
            publisher, alerts, renewer, clock,
            NullLogger<OrganisationDataExportBuildJob>.Instance)
        {
            LeaseRenewInterval = FastRenewInterval,
        };

        return new Harness(job, db, store, manifest, storage, publisher, alerts, renewer, workRoot);
    }

    private async Task<string> StatusOf(Harness h, Guid id) =>
        (await h.Store.GetAsync(id, CancellationToken.None))!.Status;

    private async Task<OrganisationDataExport> ReloadAsync(Harness h, Guid id) =>
        await h.Db.OrganisationDataExports.AsNoTracking().SingleAsync(e => e.Id == id);

    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Temp_Disk_Exhaustion_Mid_Build_Fails_The_Export_With_A_Clear_Reason_And_One_Dedup_Alert()
    {
        var manifest = new FakeManifest();
        manifest.AddFile("documents/x/big.bin", "sk-big", RandomBytes(5000));

        var h = CreateHarness(out var export, manifest: manifest,
            limits: new OrganisationDataExportResourceLimits
            {
                MaxArchiveBytesPerExport = 100,
                MinimumFreeDiskBytes = 0,
                MaxTotalWorkspaceBytes = long.MaxValue,
            });
        await using var _ = h.Db;

        await h.Job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None);

        Assert.Equal("Failed", await StatusOf(h, export.Id));
        var reloaded = await ReloadAsync(h, export.Id);
        Assert.Contains("temporary working storage", reloaded.FailureReason);

        var alert = Assert.Single(h.Alerts.Commands);
        Assert.Equal($"organisation-data-export-temp-capacity:{export.CompanyId}", alert.DedupKey);
        Assert.Null(alert.Reason);
        Assert.Equal(AdministrativeAlertCategory.ReportGeneration, alert.Category);
        Assert.Equal(AdministrativeAlertSeverity.Warning, alert.Severity);

        Assert.Equal(0, h.FakeStorage.UploadCount);
        Assert.Empty(h.Publisher.Published);
        Assert.Empty(h.WorkspaceDirs);
    }

    [Fact]
    public async Task Oversized_Streamed_Table_Trips_The_Ceiling_Mid_Entry_And_Fails_The_Export_With_One_Dedup_Alert()
    {
        // A single streamed CSV table far larger than the per-export ceiling: the budget-enforcing
        // write stream must trip DURING the entry (not only at the post-entry check), and the failure
        // must map exactly like the oversized-document case.
        var bigTable = new BigStreamedAuditSource(rowCount: 20_000, cellChars: 200);

        var h = CreateHarness(out var export, auditSource: bigTable,
            limits: new OrganisationDataExportResourceLimits
            {
                MaxArchiveBytesPerExport = 256,
                MinimumFreeDiskBytes = 0,
                MaxTotalWorkspaceBytes = long.MaxValue,
            });
        await using var _ = h.Db;

        await h.Job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None);

        Assert.Equal("Failed", await StatusOf(h, export.Id));
        var reloaded = await ReloadAsync(h, export.Id);
        Assert.Contains("temporary working storage", reloaded.FailureReason);

        var alert = Assert.Single(h.Alerts.Commands);
        Assert.Equal($"organisation-data-export-temp-capacity:{export.CompanyId}", alert.DedupKey);
        Assert.Null(alert.Reason);

        Assert.Equal(0, h.FakeStorage.UploadCount);
        Assert.Empty(h.Publisher.Published);
        Assert.Empty(h.WorkspaceDirs);

        // The generator was abandoned mid-stream, never fully enumerated.
        Assert.True(bigTable.RowsYielded < 20_000,
            $"streamed source should have been abandoned mid-stream, yielded {bigTable.RowsYielded}");
    }

    private sealed class BigStreamedAuditSource(int rowCount, int cellChars) : IAuditDataExportSource
    {
        private int _rowsYielded;
        public int RowsYielded => Volatile.Read(ref _rowsYielded);

        public Task<IReadOnlyList<DataExportTable>> GetTablesAsync(Guid companyId, CancellationToken cancellationToken)
        {
            var table = DataExportTable.Streamed("audit_log", ["A", "B"], Stream);
            return Task.FromResult<IReadOnlyList<DataExportTable>>([table]);
        }

        private async IAsyncEnumerable<IReadOnlyList<string?>> Stream(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (var i = 0; i < rowCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // High-entropy content so ZIP deflate cannot shrink it below the ceiling.
                var cell = string.Concat(Enumerable.Range(0, Math.Max(1, cellChars / 32))
                    .Select(_ => Guid.NewGuid().ToString("N")));
                yield return new string?[] { i.ToString(), cell };
                Interlocked.Increment(ref _rowsYielded);
                await Task.Yield();
            }
        }
    }

    [Fact]
    public async Task No_Concurrency_Slot_Throws_And_Leaves_The_Export_Pending_With_No_Attempt_Consumed()
    {
        var h = CreateHarness(out var export, gate: new NullGate());
        await using var _ = h.Db;

        await Assert.ThrowsAsync<OrganisationDataExportSlotUnavailableException>(() =>
            h.Job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None));

        Assert.Equal("Pending", await StatusOf(h, export.Id));
        var reloaded = await ReloadAsync(h, export.Id);
        Assert.Equal(0, reloaded.AttemptCount);
        Assert.Equal(0, h.FakeStorage.UploadCount);
        Assert.Empty(h.WorkspaceDirs);
    }

    [Fact]
    public async Task Cancellation_Mid_Stream_Deletes_The_Workspace_And_Does_Not_Upload_Or_Complete()
    {
        var manifest = new FakeManifest { OpenDelay = TimeSpan.FromMilliseconds(50) };
        for (var i = 0; i < 12; i++)
            manifest.AddFile($"documents/x/f{i}.pdf", $"sk-{i}", "A"u8.ToArray());

        var h = CreateHarness(out var export, manifest: manifest);
        await using var _ = h.Db;

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(80));

        await h.Job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, cts.Token);

        Assert.Equal(0, h.FakeStorage.UploadCount);
        Assert.Empty(h.Publisher.Published);
        Assert.Empty(h.WorkspaceDirs);
        // The attempt was claimed (InProgress) then cancelled; recovery re-enqueues it after the lease lapses.
        Assert.Equal("InProgress", await StatusOf(h, export.Id));
    }

    [Fact]
    public async Task Happy_Path_With_Real_Local_Storage_Produces_A_Valid_Zip_Of_Every_Document()
    {
        var storage = new LocalOrganisationDataExportStorage();
        var manifest = new FakeManifest();

        var expectedEntries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        for (var i = 0; i < 20; i++)
        {
            var body = Encoding.UTF8.GetBytes($"small-doc-{i}");
            manifest.AddFile($"documents/small/f{i}.txt", $"sk-s{i}", body);
            expectedEntries[$"documents/small/f{i}.txt"] = body;
        }

        var big = RandomBytes(5 * 1024 * 1024);
        manifest.AddFile("documents/big/handbook.bin", "sk-big", big);
        expectedEntries["documents/big/handbook.bin"] = big;

        var h = CreateHarness(out var export, storage: storage, manifest: manifest);
        await using var _ = h.Db;

        try
        {
            await h.Job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None);

            Assert.Equal("Completed", await StatusOf(h, export.Id));
            var reloaded = await ReloadAsync(h, export.Id);
            var key = reloaded.StorageKey!;

            await using var download = await storage.OpenAsync(key, CancellationToken.None);
            Assert.NotNull(download);
            using var buffer = new MemoryStream();
            await download!.CopyToAsync(buffer);
            var bytes = buffer.ToArray();

            Assert.Equal(reloaded.FileSizeBytes, bytes.Length);

            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            foreach (var (path, body) in expectedEntries)
            {
                var entry = zip.GetEntry(path);
                Assert.NotNull(entry);
                using var entryStream = entry!.Open();
                using var entryBuffer = new MemoryStream();
                entryStream.CopyTo(entryBuffer);
                Assert.Equal(body, entryBuffer.ToArray());
            }

            Assert.Empty(h.WorkspaceDirs);
        }
        finally
        {
            TryDeleteLocalStorage(export.CompanyId);
        }
    }

    [Fact]
    public async Task Slow_Upload_Past_The_Lease_Window_Is_Kept_Alive_By_The_Renewal_Loop_And_Completes()
    {
        var manifest = new FakeManifest();
        manifest.AddFile("documents/x/a.pdf", "sk", "A"u8.ToArray());
        var storage = new FakeStorage { UploadDelay = TimeSpan.FromMilliseconds(150) };

        var h = CreateHarness(out var export, storage: storage, manifest: manifest);
        await using var _ = h.Db;

        await h.Job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None);

        Assert.Equal("Completed", await StatusOf(h, export.Id));
        Assert.True(h.Renewer.CallCount > 1, $"renewal loop should tick during the slow upload, got {h.Renewer.CallCount}");
    }

    [Fact]
    public async Task Workspace_Is_Deleted_On_Success()
    {
        var manifest = new FakeManifest();
        manifest.AddFile("documents/x/a.pdf", "sk", "A"u8.ToArray());
        var h = CreateHarness(out var export, manifest: manifest);
        await using var _ = h.Db;

        await h.Job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None);

        Assert.Equal("Completed", await StatusOf(h, export.Id));
        Assert.Empty(h.WorkspaceDirs);
    }

    [Fact]
    public async Task Workspace_Is_Deleted_On_A_Generic_Upload_Failure()
    {
        var storage = new FakeStorage { AlwaysThrow = true };
        var h = CreateHarness(out var export, storage: storage);
        await using var _ = h.Db;

        await h.Job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None);

        Assert.Equal("Failed", await StatusOf(h, export.Id));
        Assert.Equal(3, h.FakeStorage.UploadCount);
        Assert.Empty(h.WorkspaceDirs);
    }

    private static void TryDeleteLocalStorage(Guid companyId)
    {
        try
        {
            var dir = Path.Combine(
                Path.GetTempPath(), "onebigteam", "organisation-exports",
                "organisation-exports", companyId.ToString());
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // best-effort test cleanup
        }
    }

    // ----- local fakes -----

    private sealed class PassThroughGate : IOrganisationDataExportConcurrencyGate
    {
        public int MaxConcurrentExports => int.MaxValue;

        public Task<IAsyncDisposable?> AcquireAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IAsyncDisposable?>(new Noop());

        private sealed class Noop : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class NullGate : IOrganisationDataExportConcurrencyGate
    {
        public int MaxConcurrentExports => 2;

        public Task<IAsyncDisposable?> AcquireAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IAsyncDisposable?>(null);
    }

    private sealed class RenewerSpy(IOrganisationDataExportJobStore store) : IOrganisationDataExportLeaseRenewer
    {
        private int _calls;

        public int CallCount => Volatile.Read(ref _calls);

        public Task<bool> RenewAsync(Guid exportId, Guid ownerToken, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return store.RenewLeaseAsync(exportId, ownerToken, cancellationToken);
        }
    }

    private sealed class EmptySources :
        IEmployeeDataExportSource, ILeaveDataExportSource, ISicknessDataExportSource,
        IRecruitmentDataExportSource, IAuditDataExportSource
    {
        public Task<IReadOnlyList<DataExportTable>> GetTablesAsync(Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DataExportTable>>([]);
    }

    private sealed class FakeManifest : IDocumentDataExportManifest
    {
        private readonly List<DocumentExportFileEntry> _entries = [];
        private readonly Dictionary<string, byte[]?> _content = new(StringComparer.Ordinal);

        public TimeSpan OpenDelay { get; set; } = TimeSpan.Zero;

        public void AddFile(string zipPath, string storageKey, byte[]? content)
        {
            _entries.Add(new DocumentExportFileEntry(zipPath, storageKey));
            _content[storageKey] = content;
        }

        public Task<IReadOnlyList<DataExportTable>> GetTablesAsync(Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DataExportTable>>([]);

        public Task<IReadOnlyList<DocumentExportFileEntry>> GetFileEntriesAsync(Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DocumentExportFileEntry>>(_entries.ToList());

        public async Task<Stream?> OpenDocumentAsync(Guid companyId, string storageKey, CancellationToken cancellationToken)
        {
            if (OpenDelay > TimeSpan.Zero)
                await Task.Delay(OpenDelay, cancellationToken);
            var bytes = _content.TryGetValue(storageKey, out var b) ? b : null;
            return bytes is null ? null : new MemoryStream(bytes, writable: false);
        }
    }

    private sealed class FakeStorage : IOrganisationDataExportStorage
    {
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

        public int UploadCount { get; private set; }
        public bool AlwaysThrow { get; init; }
        public TimeSpan UploadDelay { get; init; } = TimeSpan.Zero;

        public async Task<string> UploadAsync(Guid companyId, Guid exportId, Guid attemptToken, Stream content, CancellationToken cancellationToken)
        {
            UploadCount++;
            if (UploadDelay > TimeSpan.Zero)
                await Task.Delay(UploadDelay, cancellationToken);

            // Drain the stream so a streamed upload is actually exercised (and never a buffered copy).
            using var sink = new MemoryStream();
            await content.CopyToAsync(sink, cancellationToken);

            if (AlwaysThrow)
                throw new InvalidOperationException("transient storage failure");

            var key = $"organisation-exports/{companyId}/{exportId}/{attemptToken}.zip";
            _keys.Add(key);
            return key;
        }

        public Task<IReadOnlyList<string>> ListAttemptKeysAsync(Guid companyId, Guid exportId, CancellationToken cancellationToken)
        {
            var prefix = $"organisation-exports/{companyId}/{exportId}/";
            return Task.FromResult<IReadOnlyList<string>>(
                _keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList());
        }

        public Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
        {
            _keys.Remove(storageKey);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPublisher : IIntegrationEventPublisher
    {
        public List<object> Published { get; } = [];

        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
            where TEvent : IIntegrationEvent
        {
            Published.Add(integrationEvent!);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Serialises every call to the inner store behind a single gate — models the production
    /// separation of the worker's DbContext scope from the renewal loop's, which the EF in-memory
    /// provider does not tolerate when both share one context here.
    /// </summary>
    private sealed class SerializingJobStore(IOrganisationDataExportJobStore inner) : IOrganisationDataExportJobStore
    {
        private readonly SemaphoreSlim _gate = new(1, 1);

        private async Task<T> GatedAsync<T>(Func<Task<T>> op)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try { return await op().ConfigureAwait(false); }
            finally { _gate.Release(); }
        }

        private async Task GatedAsync(Func<Task> op)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try { await op().ConfigureAwait(false); }
            finally { _gate.Release(); }
        }

        public Task<OrganisationDataExportJobView?> GetAsync(Guid exportId, CancellationToken cancellationToken) =>
            GatedAsync(() => inner.GetAsync(exportId, cancellationToken));
        public Task MarkInProgressAsync(Guid exportId, CancellationToken cancellationToken) =>
            GatedAsync(() => inner.MarkInProgressAsync(exportId, cancellationToken));
        public Task<bool> BeginAttemptAsync(Guid exportId, Guid ownerToken, CancellationToken cancellationToken) =>
            GatedAsync(() => inner.BeginAttemptAsync(exportId, ownerToken, cancellationToken));
        public Task<bool> RenewLeaseAsync(Guid exportId, Guid ownerToken, CancellationToken cancellationToken) =>
            GatedAsync(() => inner.RenewLeaseAsync(exportId, ownerToken, cancellationToken));
        public Task<bool> MarkCompletedAsync(Guid exportId, Guid ownerToken, string storageKey, long fileSizeBytes, CancellationToken cancellationToken) =>
            GatedAsync(() => inner.MarkCompletedAsync(exportId, ownerToken, storageKey, fileSizeBytes, cancellationToken));
        public Task MarkFailedAsync(Guid exportId, string failureReason, CancellationToken cancellationToken) =>
            GatedAsync(() => inner.MarkFailedAsync(exportId, failureReason, cancellationToken));
        public Task<bool> MarkFailedAsync(Guid exportId, Guid ownerToken, string failureReason, CancellationToken cancellationToken) =>
            GatedAsync(() => inner.MarkFailedAsync(exportId, ownerToken, failureReason, cancellationToken));
        public Task<bool> MarkFailedDueToMissingDocumentsAsync(Guid exportId, Guid ownerToken, int missingCount, CancellationToken cancellationToken) =>
            GatedAsync(() => inner.MarkFailedDueToMissingDocumentsAsync(exportId, ownerToken, missingCount, cancellationToken));
        public Task ResetForRetryAsync(Guid exportId, CancellationToken cancellationToken) =>
            GatedAsync(() => inner.ResetForRetryAsync(exportId, cancellationToken));
        public Task<bool> ClaimForRecoveryAsync(Guid exportId, Guid recoveryToken, CancellationToken cancellationToken) =>
            GatedAsync(() => inner.ClaimForRecoveryAsync(exportId, recoveryToken, cancellationToken));
        public Task<bool> ResetForRetryAsync(Guid exportId, Guid recoveryToken, CancellationToken cancellationToken) =>
            GatedAsync(() => inner.ResetForRetryAsync(exportId, recoveryToken, cancellationToken));
        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetArtefactCleanupCandidatesAsync(int batchSize, CancellationToken cancellationToken) =>
            GatedAsync(() => inner.GetArtefactCleanupCandidatesAsync(batchSize, cancellationToken));
        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetRecentlyCleanedArtefactsAsync(int batchSize, CancellationToken cancellationToken) =>
            GatedAsync(() => inner.GetRecentlyCleanedArtefactsAsync(batchSize, cancellationToken));
        public Task MarkAttemptFilesCleanedAsync(Guid exportId, CancellationToken cancellationToken) =>
            GatedAsync(() => inner.MarkAttemptFilesCleanedAsync(exportId, cancellationToken));
        public Task DeferArtefactCleanupAsync(Guid exportId, CancellationToken cancellationToken) =>
            GatedAsync(() => inner.DeferArtefactCleanupAsync(exportId, cancellationToken));
        public Task RecordLateUploadRecheckAsync(Guid exportId, bool succeeded, CancellationToken cancellationToken) =>
            GatedAsync(() => inner.RecordLateUploadRecheckAsync(exportId, succeeded, cancellationToken));
        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetRecoverableAsync(
            DateTimeOffset pendingQueuedBefore, DateTimeOffset leaseExpiredAsOf, CancellationToken cancellationToken) =>
            GatedAsync(() => inner.GetRecoverableAsync(pendingQueuedBefore, leaseExpiredAsOf, cancellationToken));
        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetExpiredAsync(CancellationToken cancellationToken) =>
            GatedAsync(() => inner.GetExpiredAsync(cancellationToken));
        public Task MarkExpiredAsync(Guid exportId, CancellationToken cancellationToken) =>
            GatedAsync(() => inner.MarkExpiredAsync(exportId, cancellationToken));
    }
}
