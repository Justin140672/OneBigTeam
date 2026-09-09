using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Jobs;
using HR.Modules.Reporting.Persistence;
using HR.Modules.Reporting.Services;
using HR.Modules.Reporting.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Reporting.Tests.Jobs;

public class OrganisationDataExportBuildJobTests
{
    private static readonly DateTime Now = new(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);

    private static ReportingDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<ReportingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private sealed record Harness(
        OrganisationDataExportBuildJob Job,
        ReportingDbContext Db,
        IOrganisationDataExportJobStore Store,
        FakeDocumentManifest Manifest,
        FakeExportStorage Storage,
        RecordingIntegrationEventPublisher Publisher,
        CapturingAdministrativeAlertWriter Alerts,
        FakeLeaseRenewer Renewer);

    private static readonly TimeSpan FastRenewInterval = TimeSpan.FromMilliseconds(20);

    private static Harness CreateHarness(
        out OrganisationDataExport export,
        Guid? companyId = null,
        FakeExportStorage? storage = null,
        FakeDocumentManifest? manifest = null,
        Func<IOrganisationDataExportJobStore, IOrganisationDataExportJobStore>? decorateStore = null,
        Action<FakeLeaseRenewer>? configureRenewer = null)
    {
        var db = BuildContext();
        var company = companyId ?? Guid.NewGuid();
        export = OrganisationDataExport.Create(company, Guid.NewGuid(), "Admin", new DateTimeOffset(Now.AddMinutes(-10)));
        db.OrganisationDataExports.Add(export);
        db.SaveChanges();

        var clock = new FakeClock(Now);

        // The production build job renews its lease on a separate DbContext scope, so the worker and the
        // renewal loop never touch one EF context concurrently. In this harness both paths share the
        // single in-memory ReportingDbContext, so serialise every store call to model that isolation and
        // keep the renewal-loop tests deterministic at high parallelism.
        IOrganisationDataExportJobStore store = new SerializingJobStore(new OrganisationDataExportJobStore(db, clock));
        if (decorateStore is not null)
            store = decorateStore(store);

        storage ??= new FakeExportStorage();
        manifest ??= new FakeDocumentManifest();
        var publisher = new RecordingIntegrationEventPublisher();
        var alerts = new CapturingAdministrativeAlertWriter();
        var sources = new EmptyExportSources();

        // Follow-up G: the renewal loop renews on a distinct code path from the worker's own store.
        // This fake delegates to the (possibly decorated) shared store's RenewLeaseAsync but can be
        // told to force a takeover or to throw transiently.
        var renewer = new FakeLeaseRenewer(store);
        configureRenewer?.Invoke(renewer);

        var job = new OrganisationDataExportBuildJob(
            store,
            sources,
            sources,
            sources,
            sources,
            sources,
            manifest,
            new OrganisationDataExportPackageBuilder(),
            storage,
            publisher,
            alerts,
            renewer,
            clock,
            NullLogger<OrganisationDataExportBuildJob>.Instance)
        {
            LeaseRenewInterval = FastRenewInterval,
        };

        return new Harness(job, db, store, manifest, storage, publisher, alerts, renewer);
    }

    private async Task<string> StatusOf(Harness h, Guid id) =>
        (await h.Store.GetAsync(id, CancellationToken.None))!.Status;

    [Fact]
    public async Task Happy_Path_Uploads_Completes_And_Publishes_Completed_Event()
    {
        var manifest = new FakeDocumentManifest();
        manifest.AddFile("documents/Contracts/offer.pdf", "sk-1", "PDF-BYTES"u8.ToArray());
        var h = CreateHarness(out var export, manifest: manifest);
        await using var _ = h.Db;

        await h.Job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None);

        Assert.Equal("Completed", await StatusOf(h, export.Id));
        Assert.Equal(1, h.Storage.UploadCount);
        Assert.Single(h.Publisher.Published.OfType<OrganisationDataExportCompletedIntegrationEvent>());
        Assert.Empty(h.Alerts.Commands);

        // Follow-up D: the published key is a per-attempt object under .../{exportId}/...
        var publishedKey = (await h.Store.GetAsync(export.Id, CancellationToken.None))!.StorageKey!;
        Assert.Contains($"/{export.Id}/", publishedKey);
        Assert.Equal(new[] { publishedKey }, h.Storage.Keys.ToArray());
    }

    [Fact]
    public async Task Heartbeat_Renews_The_Lease_During_A_Long_Build()
    {
        var manifest = new FakeDocumentManifest { OpenDelay = TimeSpan.FromMilliseconds(15) };
        for (var i = 0; i < 12; i++)
            manifest.AddFile($"documents/x/f{i}.pdf", $"sk-{i}", "A"u8.ToArray());

        HeartbeatCountingJobStore? counting = null;
        var h = CreateHarness(out var export, manifest: manifest,
            decorateStore: inner => counting = new HeartbeatCountingJobStore(inner));
        await using var _ = h.Db;

        await h.Job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None);

        Assert.Equal("Completed", await StatusOf(h, export.Id));
        Assert.NotNull(counting);
        Assert.True(counting!.RenewCount > 1, $"expected multiple heartbeats, got {counting.RenewCount}");
    }

    [Fact]
    public async Task Ownership_Lost_Mid_Build_Aborts_Cleanly()
    {
        var manifest = new FakeDocumentManifest { OpenDelay = TimeSpan.FromMilliseconds(40) };
        for (var i = 0; i < 4; i++)
            manifest.AddFile($"documents/x/a{i}.pdf", $"sk{i}", "A"u8.ToArray());

        HeartbeatCountingJobStore? counting = null;
        var h = CreateHarness(out var export, manifest: manifest,
            decorateStore: inner => counting = new HeartbeatCountingJobStore(inner, failRenewOnCall: 2));
        await using var _ = h.Db;

        await h.Job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None);

        Assert.Equal("InProgress", await StatusOf(h, export.Id));
        Assert.Equal(0, h.Storage.UploadCount);
        Assert.Empty(h.Publisher.Published);
        Assert.Equal(0, counting!.OwnedMarkFailedCount);
        Assert.Equal(0, counting.SystemMarkFailedCount);
    }

    [Fact]
    public async Task Superseded_Worker_Cannot_Mark_Failed()
    {
        var storage = new FakeExportStorage { AlwaysThrow = true };
        HeartbeatCountingJobStore? counting = null;
        var h = CreateHarness(out var export, storage: storage,
            decorateStore: inner => counting = new HeartbeatCountingJobStore(inner, denyOwnedMarkFailed: true));
        await using var _ = h.Db;

        await h.Job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None);

        Assert.NotEqual("Failed", await StatusOf(h, export.Id));
        Assert.Empty(h.Publisher.Published);
        Assert.True(counting!.OwnedMarkFailedCount > 0);
    }

    [Fact]
    public async Task Winner_Sweeps_Up_Sibling_Attempt_Archives()
    {
        var manifest = new FakeDocumentManifest();
        manifest.AddFile("documents/x/a.pdf", "sk", "A"u8.ToArray());
        var storage = new FakeExportStorage();
        var h = CreateHarness(out var export, storage: storage, manifest: manifest);
        await using var _ = h.Db;

        var orphan = FakeExportStorage.KeyFor(export.CompanyId, export.Id, Guid.NewGuid());
        storage.SeedKey(orphan);

        await h.Job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None);

        var publishedKey = (await h.Store.GetAsync(export.Id, CancellationToken.None))!.StorageKey!;
        Assert.Equal(new[] { publishedKey }, storage.Keys.ToArray());
        Assert.DoesNotContain(orphan, storage.Keys);
    }

    [Fact]
    public async Task Duplicate_Execution_When_Claim_Fails_Is_A_No_Op()
    {
        var h = CreateHarness(out var export,
            decorateStore: inner => new ClaimRefusingJobStore(inner));
        await using var _ = h.Db;

        await h.Job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None);

        Assert.Equal("Pending", await StatusOf(h, export.Id));
        Assert.Equal(0, h.Storage.UploadCount);
        Assert.Empty(h.Publisher.Published);
    }

    [Fact]
    public async Task Lease_Lost_Before_Upload_Abandons_The_Attempt_Without_Uploading_Or_Publishing()
    {
        var manifest = new FakeDocumentManifest { OpenDelay = TimeSpan.FromMilliseconds(40) };
        for (var i = 0; i < 4; i++)
            manifest.AddFile($"documents/x/a{i}.pdf", $"sk{i}", "A"u8.ToArray());
        var h = CreateHarness(out var export, manifest: manifest,
            decorateStore: inner => new LeaseDenyingJobStore(inner, denyRenew: true));
        await using var _ = h.Db;

        await h.Job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None);

        Assert.Equal("InProgress", await StatusOf(h, export.Id));
        Assert.Equal(0, h.Storage.UploadCount);
        Assert.Empty(h.Publisher.Published);
    }

    [Fact]
    public async Task Lease_Lost_Before_Completion_Does_Not_Publish_The_Completed_Event()
    {
        var manifest = new FakeDocumentManifest();
        manifest.AddFile("documents/x/a.pdf", "sk", "A"u8.ToArray());
        var h = CreateHarness(out var export, manifest: manifest,
            decorateStore: inner => new LeaseDenyingJobStore(inner, denyComplete: true));
        await using var _ = h.Db;

        await h.Job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None);

        Assert.NotEqual("Completed", await StatusOf(h, export.Id));
        Assert.Empty(h.Publisher.Published);
    }

    [Fact]
    public async Task Terminal_Row_Is_Skipped_Without_Failing_Or_Uploading()
    {
        var h = CreateHarness(out var export);
        await using var _ = h.Db;
        var seedToken = Guid.NewGuid();
        export.BeginAttempt(seedToken, new DateTimeOffset(Now));
        export.MarkCompleted(seedToken, "existing-key", 5, new DateTimeOffset(Now));
        await h.Db.SaveChangesAsync();

        await h.Job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None);

        Assert.Equal("Completed", await StatusOf(h, export.Id));
        Assert.Equal(0, h.Storage.UploadCount);
        Assert.Empty(h.Publisher.Published);
    }

    [Fact]
    public async Task Company_Id_Mismatch_Throws()
    {
        var h = CreateHarness(out var export);
        await using var _ = h.Db;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Job.RunAsync(export.Id, Guid.NewGuid(), export.RequestedByUserId, CancellationToken.None));
    }

    [Fact]
    public async Task Missing_Documents_Fail_The_Export_Raise_An_Alert_And_Never_Upload()
    {
        var manifest = new FakeDocumentManifest();
        manifest.AddFile("documents/Contracts/present.pdf", "sk-ok", "OK"u8.ToArray());
        manifest.AddFile("documents/Contracts/gone.pdf", "sk-missing", content: null);
        manifest.AddFile("documents/Contracts/gone2.pdf", "sk-missing-2", content: null);
        var h = CreateHarness(out var export, manifest: manifest);
        await using var _ = h.Db;

        await h.Job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None);

        Assert.Equal("Failed", await StatusOf(h, export.Id));
        var reloaded = await h.Db.OrganisationDataExports.AsNoTracking().SingleAsync(e => e.Id == export.Id);
        Assert.Equal(2, reloaded.MissingDocumentCount);

        var alert = Assert.Single(h.Alerts.Commands);
        Assert.Equal(AdministrativeAlertCategory.ReportGeneration, alert.Category);
        Assert.Equal(AdministrativeAlertSeverity.Warning, alert.Severity);
        Assert.Equal($"organisation-data-export-missing-documents:{export.CompanyId}", alert.DedupKey);
        // Follow-up F: the missing-documents alert is the one failure reason that queues an
        // internal-operations notification email.
        Assert.Equal(AdministrativeAlertReason.MissingDocumentExport, alert.Reason);
        Assert.Equal(2, alert.AffectedItemCount);

        Assert.Equal(0, h.Storage.UploadCount);
        Assert.Empty(h.Publisher.Published);
        Assert.Equal(
            new[] { "GetFileEntriesAsync", "GetTablesAsync", "OpenDocumentAsync" },
            h.Manifest.InvokedMethods.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(h.Manifest.InvokedMethods, m => m.Contains("Delete", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Transient_Storage_Failures_Are_Retried_And_The_Export_Still_Completes()
    {
        var manifest = new FakeDocumentManifest();
        manifest.AddFile("documents/x/a.pdf", "sk", "A"u8.ToArray());
        var storage = new FakeExportStorage { FailuresBeforeSuccess = 2 };
        var h = CreateHarness(out var export, storage: storage, manifest: manifest);
        await using var _ = h.Db;

        await h.Job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None);

        Assert.Equal("Completed", await StatusOf(h, export.Id));
        Assert.Equal(3, storage.UploadCount);
        Assert.Single(h.Publisher.Published.OfType<OrganisationDataExportCompletedIntegrationEvent>());
    }

    [Fact]
    public async Task Persistent_Storage_Failure_Marks_Failed_And_Publishes_Nothing()
    {
        var storage = new FakeExportStorage { AlwaysThrow = true };
        var h = CreateHarness(out var export, storage: storage);
        await using var _ = h.Db;

        await h.Job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None);

        Assert.Equal("Failed", await StatusOf(h, export.Id));
        Assert.Equal(3, storage.UploadCount);
        Assert.Empty(h.Publisher.Published);
    }

    // ----- Ticket G: continuous background lease renewal -----

    [Fact]
    public async Task Build_That_Runs_Past_The_Lease_Window_Keeps_Ownership_And_Still_Completes()
    {
        var manifest = new FakeDocumentManifest { OpenDelay = TimeSpan.FromMilliseconds(25) };
        for (var i = 0; i < 6; i++)
            manifest.AddFile($"documents/x/f{i}.pdf", $"sk-{i}", "A"u8.ToArray());
        var h = CreateHarness(out var export, manifest: manifest);
        await using var _ = h.Db;

        await h.Job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None);

        Assert.Equal("Completed", await StatusOf(h, export.Id));
        Assert.Equal(1, h.Storage.UploadCount);
        Assert.True(h.Renewer.CallCount > 1, $"renewal loop should have ticked repeatedly, got {h.Renewer.CallCount}");
        Assert.Single(h.Publisher.Published.OfType<OrganisationDataExportCompletedIntegrationEvent>());
    }

    [Fact]
    public async Task Slow_Upload_Past_The_Lease_Window_Is_Kept_Alive_By_The_Renewal_Loop()
    {
        var manifest = new FakeDocumentManifest();
        manifest.AddFile("documents/x/a.pdf", "sk", "A"u8.ToArray());
        var storage = new FakeExportStorage { UploadDelay = TimeSpan.FromMilliseconds(150) };
        var h = CreateHarness(out var export, storage: storage, manifest: manifest);
        await using var _ = h.Db;

        await h.Job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None);

        Assert.Equal("Completed", await StatusOf(h, export.Id));
        Assert.True(h.Renewer.CallCount > 1, $"renewal loop should tick during the slow upload, got {h.Renewer.CallCount}");
    }

    [Fact]
    public async Task Renewer_Reports_Takeover_Mid_Build_Then_Nothing_Is_Uploaded_Completed_Or_Failed()
    {
        var manifest = new FakeDocumentManifest { OpenDelay = TimeSpan.FromMilliseconds(40) };
        for (var i = 0; i < 5; i++)
            manifest.AddFile($"documents/x/f{i}.pdf", $"sk-{i}", "A"u8.ToArray());
        var h = CreateHarness(out var export, manifest: manifest,
            configureRenewer: r => r.ForceTakeoverAfter = 2);
        await using var _ = h.Db;

        await h.Job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None);

        Assert.Equal("InProgress", await StatusOf(h, export.Id));
        Assert.Equal(0, h.Storage.UploadCount);
        Assert.Empty(h.Publisher.Published);
        Assert.Empty(h.Alerts.Commands);
    }

    [Fact]
    public async Task Transient_Renewal_Failures_Then_Recovery_Do_Not_Stop_The_Export_Completing()
    {
        var manifest = new FakeDocumentManifest { OpenDelay = TimeSpan.FromMilliseconds(25) };
        for (var i = 0; i < 6; i++)
            manifest.AddFile($"documents/x/f{i}.pdf", $"sk-{i}", "A"u8.ToArray());
        var h = CreateHarness(out var export, manifest: manifest,
            configureRenewer: r => r.TransientThrowsRemaining = 2);
        await using var _ = h.Db;

        await h.Job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None);

        Assert.Equal("Completed", await StatusOf(h, export.Id));
        Assert.Single(h.Publisher.Published.OfType<OrganisationDataExportCompletedIntegrationEvent>());
    }

    [Fact]
    public async Task Renewal_Loop_Uses_The_Injected_Renewer_Not_The_Worker_Store_Directly()
    {
        var manifest = new FakeDocumentManifest { OpenDelay = TimeSpan.FromMilliseconds(25) };
        for (var i = 0; i < 5; i++)
            manifest.AddFile($"documents/x/f{i}.pdf", $"sk-{i}", "A"u8.ToArray());

        HeartbeatCountingJobStore? counting = null;
        var h = CreateHarness(out var export, manifest: manifest,
            decorateStore: inner => counting = new HeartbeatCountingJobStore(inner));
        await using var _ = h.Db;

        await h.Job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None);

        // Every renewal observed by the store arrived via the renewer abstraction (separate scope in
        // production), and the renewer was exercised more than once.
        Assert.True(h.Renewer.CallCount > 1);
        Assert.True(counting!.RenewCount > 1);
        Assert.Equal(h.Renewer.RenewsDelegatedToStore, counting.RenewCount);
    }

    // ----- Ticket 3J: renewal drained before terminal writes / interleaved renewal races -----

    [Fact]
    public async Task Renewed_Mid_Build_Then_Missing_Documents_Still_Fails_The_Export_And_Raises_The_Alert()
    {
        var manifest = new FakeDocumentManifest { OpenDelay = TimeSpan.FromMilliseconds(25) };
        manifest.AddFile("documents/Contracts/present.pdf", "sk-ok", "OK"u8.ToArray());
        for (var i = 0; i < 4; i++)
            manifest.AddFile($"documents/Contracts/gone{i}.pdf", $"sk-missing-{i}", content: null);

        HeartbeatCountingJobStore? counting = null;
        var h = CreateHarness(out var export, manifest: manifest,
            decorateStore: inner => counting = new HeartbeatCountingJobStore(inner));
        await using var _ = h.Db;

        await h.Job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None);

        Assert.Equal("Failed", await StatusOf(h, export.Id));
        var reloaded = await h.Db.OrganisationDataExports.AsNoTracking().SingleAsync(e => e.Id == export.Id);
        Assert.Equal(4, reloaded.MissingDocumentCount);

        Assert.True(h.Renewer.CallCount > 1, $"renewal loop should have ticked before the failure, got {h.Renewer.CallCount}");
        Assert.True(counting!.RenewCount > 1);
        Assert.Equal(1, counting.MissingDocsMarkFailedCount);

        var alert = Assert.Single(h.Alerts.Commands);
        Assert.Equal(AdministrativeAlertReason.MissingDocumentExport, alert.Reason);
        Assert.Equal(4, alert.AffectedItemCount);
        Assert.Equal(0, h.Storage.UploadCount);
        Assert.Empty(h.Publisher.Published);
    }

    [Fact]
    public async Task Superseded_Missing_Documents_Worker_Does_Not_Raise_The_Alert()
    {
        var manifest = new FakeDocumentManifest();
        manifest.AddFile("documents/Contracts/gone.pdf", "sk-missing", content: null);

        HeartbeatCountingJobStore? counting = null;
        var h = CreateHarness(out var export, manifest: manifest,
            decorateStore: inner => counting = new HeartbeatCountingJobStore(inner, denyMissingDocsMarkFailed: true));
        await using var _ = h.Db;

        await h.Job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None);

        Assert.Equal(1, counting!.MissingDocsMarkFailedCount);
        Assert.NotEqual("Failed", await StatusOf(h, export.Id));
        Assert.Empty(h.Alerts.Commands);
        Assert.Empty(h.Publisher.Published);
    }

    [Fact]
    public async Task A_Renewal_Committing_Between_Reload_And_Save_Does_Not_Force_A_Rebuild_Or_Block_Completion()
    {
        var manifest = new FakeDocumentManifest();
        manifest.AddFile("documents/x/a.pdf", "sk", "A"u8.ToArray());
        var h = CreateHarness(out var export, manifest: manifest,
            decorateStore: inner => new RenewInterleavingJobStore(inner));
        await using var _ = h.Db;

        await h.Job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None);

        Assert.Equal("Completed", await StatusOf(h, export.Id));
        Assert.Equal(1, h.Storage.UploadCount);
        Assert.Single(h.Publisher.Published.OfType<OrganisationDataExportCompletedIntegrationEvent>());
    }

    [Fact]
    public async Task Full_Build_With_The_Real_Scoped_Lease_Renewer_Heartbeats_On_A_Separate_Scope_And_Completes_Once()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var clock = new FakeClock(Now);

        var services = new ServiceCollection();
        services.AddDbContext<ReportingDbContext>(o => o.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped);
        services.AddSingleton<IClock>(clock);
        services.AddScoped<IOrganisationDataExportJobStore>(sp =>
            new OrganisationDataExportJobStore(sp.GetRequiredService<ReportingDbContext>(), clock));
        await using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        OrganisationDataExport export;
        await using (var seedScope = scopeFactory.CreateAsyncScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<ReportingDbContext>();
            export = OrganisationDataExport.Create(Guid.NewGuid(), Guid.NewGuid(), "Admin", new DateTimeOffset(Now.AddMinutes(-10)));
            seedDb.OrganisationDataExports.Add(export);
            await seedDb.SaveChangesAsync();
        }

        // The worker uses its own DbContext/store, distinct from the renewer's per-tick scope.
        await using var workerDb = new ReportingDbContext(
            new DbContextOptionsBuilder<ReportingDbContext>().UseInMemoryDatabase(dbName).Options);
        var workerStore = new OrganisationDataExportJobStore(workerDb, clock);
        var renewer = new ScopedOrganisationDataExportLeaseRenewer(scopeFactory);

        var manifest = new FakeDocumentManifest { OpenDelay = TimeSpan.FromMilliseconds(25) };
        for (var i = 0; i < 6; i++)
            manifest.AddFile($"documents/x/f{i}.pdf", $"sk-{i}", "A"u8.ToArray());
        var storage = new FakeExportStorage();
        var publisher = new RecordingIntegrationEventPublisher();
        var sources = new EmptyExportSources();

        var job = new OrganisationDataExportBuildJob(
            workerStore, sources, sources, sources, sources, sources, manifest,
            new OrganisationDataExportPackageBuilder(), storage, publisher,
            new CapturingAdministrativeAlertWriter(), renewer, clock,
            NullLogger<OrganisationDataExportBuildJob>.Instance)
        {
            LeaseRenewInterval = FastRenewInterval,
        };

        await job.RunAsync(export.Id, export.CompanyId, export.RequestedByUserId, CancellationToken.None);

        var view = await workerStore.GetAsync(export.Id, CancellationToken.None);
        Assert.Equal("Completed", view!.Status);
        Assert.Equal(1, storage.UploadCount);
        Assert.Single(publisher.Published.OfType<OrganisationDataExportCompletedIntegrationEvent>());
    }

    // ----- fakes -----

    /// <summary>
    /// Ticket 3J: simulates a heartbeat that commits <i>after</i> the store has captured its expected
    /// version but before the terminal write persists — the first <see cref="MarkCompletedAsync"/> call
    /// renews the same owner's lease (bumping the row version) and then delegates. The store's
    /// <c>ApplyOwnedTransitionAsync</c> retry loop must still complete without rebuilding.
    /// </summary>
    private sealed class RenewInterleavingJobStore(IOrganisationDataExportJobStore inner) : IOrganisationDataExportJobStore
    {
        private bool _interleaved;

        public async Task<bool> MarkCompletedAsync(Guid exportId, Guid ownerToken, string storageKey, long fileSizeBytes, CancellationToken cancellationToken)
        {
            if (!_interleaved)
            {
                _interleaved = true;
                await inner.RenewLeaseAsync(exportId, ownerToken, cancellationToken);
            }

            return await inner.MarkCompletedAsync(exportId, ownerToken, storageKey, fileSizeBytes, cancellationToken);
        }

        public Task<OrganisationDataExportJobView?> GetAsync(Guid exportId, CancellationToken cancellationToken) =>
            inner.GetAsync(exportId, cancellationToken);
        public Task MarkInProgressAsync(Guid exportId, CancellationToken cancellationToken) =>
            inner.MarkInProgressAsync(exportId, cancellationToken);
        public Task<bool> BeginAttemptAsync(Guid exportId, Guid ownerToken, CancellationToken cancellationToken) =>
            inner.BeginAttemptAsync(exportId, ownerToken, cancellationToken);
        public Task<bool> RenewLeaseAsync(Guid exportId, Guid ownerToken, CancellationToken cancellationToken) =>
            inner.RenewLeaseAsync(exportId, ownerToken, cancellationToken);
        public Task MarkFailedAsync(Guid exportId, string failureReason, CancellationToken cancellationToken) =>
            inner.MarkFailedAsync(exportId, failureReason, cancellationToken);
        public Task<bool> MarkFailedAsync(Guid exportId, Guid ownerToken, string failureReason, CancellationToken cancellationToken) =>
            inner.MarkFailedAsync(exportId, ownerToken, failureReason, cancellationToken);
        public Task<bool> MarkFailedDueToMissingDocumentsAsync(Guid exportId, Guid ownerToken, int missingCount, CancellationToken cancellationToken) =>
            inner.MarkFailedDueToMissingDocumentsAsync(exportId, ownerToken, missingCount, cancellationToken);
        public Task ResetForRetryAsync(Guid exportId, CancellationToken cancellationToken) =>
            inner.ResetForRetryAsync(exportId, cancellationToken);
        public Task<bool> ClaimForRecoveryAsync(Guid exportId, Guid recoveryToken, CancellationToken cancellationToken) =>
            inner.ClaimForRecoveryAsync(exportId, recoveryToken, cancellationToken);
        public Task<bool> ResetForRetryAsync(Guid exportId, Guid recoveryToken, CancellationToken cancellationToken) =>
            inner.ResetForRetryAsync(exportId, recoveryToken, cancellationToken);
        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetArtefactCleanupCandidatesAsync(int batchSize, CancellationToken cancellationToken) =>
            inner.GetArtefactCleanupCandidatesAsync(batchSize, cancellationToken);
        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetRecentlyCleanedArtefactsAsync(int batchSize, CancellationToken cancellationToken) =>
            inner.GetRecentlyCleanedArtefactsAsync(batchSize, cancellationToken);
        public Task MarkAttemptFilesCleanedAsync(Guid exportId, CancellationToken cancellationToken) =>
            inner.MarkAttemptFilesCleanedAsync(exportId, cancellationToken);
        public Task DeferArtefactCleanupAsync(Guid exportId, CancellationToken cancellationToken) =>
            inner.DeferArtefactCleanupAsync(exportId, cancellationToken);
        public Task RecordLateUploadRecheckAsync(Guid exportId, bool succeeded, CancellationToken cancellationToken) =>
            inner.RecordLateUploadRecheckAsync(exportId, succeeded, cancellationToken);
        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetRecoverableAsync(
            DateTimeOffset pendingQueuedBefore, DateTimeOffset leaseExpiredAsOf, CancellationToken cancellationToken) =>
            inner.GetRecoverableAsync(pendingQueuedBefore, leaseExpiredAsOf, cancellationToken);
        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetExpiredAsync(CancellationToken cancellationToken) =>
            inner.GetExpiredAsync(cancellationToken);
        public Task MarkExpiredAsync(Guid exportId, CancellationToken cancellationToken) =>
            inner.MarkExpiredAsync(exportId, cancellationToken);
    }

    private sealed class FakeLeaseRenewer(IOrganisationDataExportJobStore store) : IOrganisationDataExportLeaseRenewer
    {
        private int _calls;
        private int _delegated;

        public int CallCount => Volatile.Read(ref _calls);
        public int RenewsDelegatedToStore => Volatile.Read(ref _delegated);

        /// <summary>Force every renewal to report a takeover.</summary>
        public bool ForceTakeover { get; set; }

        /// <summary>Report a takeover from the Nth renewal onwards.</summary>
        public int ForceTakeoverAfter { get; set; } = int.MaxValue;

        /// <summary>Throw a transient error for the next N renewals before behaving normally.</summary>
        public int TransientThrowsRemaining { get; set; }

        public async Task<bool> RenewAsync(Guid exportId, Guid ownerToken, CancellationToken cancellationToken)
        {
            var n = Interlocked.Increment(ref _calls);

            if (TransientThrowsRemaining > 0)
            {
                TransientThrowsRemaining--;
                throw new InvalidOperationException("transient renewal failure");
            }

            if (ForceTakeover || n >= ForceTakeoverAfter)
                return false;

            Interlocked.Increment(ref _delegated);
            return await store.RenewLeaseAsync(exportId, ownerToken, cancellationToken);
        }
    }

    private sealed class EmptyExportSources :
        IEmployeeDataExportSource, ILeaveDataExportSource, ISicknessDataExportSource,
        IRecruitmentDataExportSource, IAuditDataExportSource
    {
        public Task<IReadOnlyList<DataExportTable>> GetTablesAsync(Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DataExportTable>>([]);
    }

    private sealed class FakeDocumentManifest : IDocumentDataExportManifest
    {
        private readonly List<DocumentExportFileEntry> _entries = [];
        private readonly Dictionary<string, byte[]?> _content = new(StringComparer.Ordinal);

        public List<string> InvokedMethods { get; } = [];

        /// <summary>Ticket G: slows each document read so the background renewal loop ticks during the build.</summary>
        public TimeSpan OpenDelay { get; set; } = TimeSpan.Zero;

        public void AddFile(string zipPath, string storageKey, byte[]? content)
        {
            _entries.Add(new DocumentExportFileEntry(zipPath, storageKey));
            _content[storageKey] = content;
        }

        public Task<IReadOnlyList<DataExportTable>> GetTablesAsync(Guid companyId, CancellationToken cancellationToken)
        {
            InvokedMethods.Add(nameof(GetTablesAsync));
            return Task.FromResult<IReadOnlyList<DataExportTable>>([]);
        }

        public Task<IReadOnlyList<DocumentExportFileEntry>> GetFileEntriesAsync(Guid companyId, CancellationToken cancellationToken)
        {
            InvokedMethods.Add(nameof(GetFileEntriesAsync));
            return Task.FromResult<IReadOnlyList<DocumentExportFileEntry>>(_entries.ToList());
        }

        public async Task<Stream?> OpenDocumentAsync(Guid companyId, string storageKey, CancellationToken cancellationToken)
        {
            InvokedMethods.Add(nameof(OpenDocumentAsync));
            if (OpenDelay > TimeSpan.Zero)
                await Task.Delay(OpenDelay, cancellationToken);
            var bytes = _content.TryGetValue(storageKey, out var b) ? b : null;
            return bytes is null ? null : new MemoryStream(bytes, writable: false);
        }
    }

    private sealed class FakeExportStorage : IOrganisationDataExportStorage
    {
        // Follow-up D: per-attempt key convention organisation-exports/{companyId}/{exportId}/{attemptToken}.zip
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

        public int UploadCount { get; private set; }
        public int FailuresBeforeSuccess { get; init; }
        public bool AlwaysThrow { get; init; }

        /// <summary>Ticket G: slows the upload so the background renewal loop ticks during it.</summary>
        public TimeSpan UploadDelay { get; init; } = TimeSpan.Zero;

        public IReadOnlyCollection<string> Keys => _keys;

        /// <summary>Pre-seed an orphan attempt archive left behind by a superseded worker.</summary>
        public void SeedKey(string key) => _keys.Add(key);

        public static string KeyFor(Guid companyId, Guid exportId, Guid attemptToken) =>
            $"organisation-exports/{companyId}/{exportId}/{attemptToken}.zip";

        public async Task<string> UploadAsync(Guid companyId, Guid exportId, Guid attemptToken, Stream content, CancellationToken cancellationToken)
        {
            UploadCount++;
            if (UploadDelay > TimeSpan.Zero)
                await Task.Delay(UploadDelay, cancellationToken);
            if (AlwaysThrow || UploadCount <= FailuresBeforeSuccess)
                throw new InvalidOperationException("transient storage failure");
            var key = KeyFor(companyId, exportId, attemptToken);
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

    /// <summary>
    /// Follow-up D: decorates a real job store, counts <see cref="RenewLeaseAsync"/> calls and can be
    /// told to fail the Nth renewal (simulating a replacement worker taking over mid-build) and/or to
    /// force the ownership-guarded <see cref="MarkFailedAsync(Guid,Guid,string,CancellationToken)"/>
    /// to return false (simulating a superseded worker that can no longer record a failure).
    /// </summary>
    private sealed class HeartbeatCountingJobStore(
        IOrganisationDataExportJobStore inner,
        int? failRenewOnCall = null,
        bool denyOwnedMarkFailed = false,
        bool denyMissingDocsMarkFailed = false)
        : IOrganisationDataExportJobStore
    {
        public int RenewCount { get; private set; }
        public int OwnedMarkFailedCount { get; private set; }
        public int SystemMarkFailedCount { get; private set; }
        public int MissingDocsMarkFailedCount { get; private set; }

        public Task<bool> RenewLeaseAsync(Guid exportId, Guid ownerToken, CancellationToken cancellationToken)
        {
            RenewCount++;
            if (failRenewOnCall is { } n && RenewCount >= n)
                return Task.FromResult(false);
            return inner.RenewLeaseAsync(exportId, ownerToken, cancellationToken);
        }

        public Task<bool> MarkFailedAsync(Guid exportId, Guid ownerToken, string failureReason, CancellationToken cancellationToken)
        {
            OwnedMarkFailedCount++;
            return denyOwnedMarkFailed
                ? Task.FromResult(false)
                : inner.MarkFailedAsync(exportId, ownerToken, failureReason, cancellationToken);
        }

        public Task MarkFailedAsync(Guid exportId, string failureReason, CancellationToken cancellationToken)
        {
            SystemMarkFailedCount++;
            return inner.MarkFailedAsync(exportId, failureReason, cancellationToken);
        }

        public Task<OrganisationDataExportJobView?> GetAsync(Guid exportId, CancellationToken cancellationToken) =>
            inner.GetAsync(exportId, cancellationToken);
        public Task MarkInProgressAsync(Guid exportId, CancellationToken cancellationToken) =>
            inner.MarkInProgressAsync(exportId, cancellationToken);
        public Task<bool> BeginAttemptAsync(Guid exportId, Guid ownerToken, CancellationToken cancellationToken) =>
            inner.BeginAttemptAsync(exportId, ownerToken, cancellationToken);
        public Task<bool> MarkCompletedAsync(Guid exportId, Guid ownerToken, string storageKey, long fileSizeBytes, CancellationToken cancellationToken) =>
            inner.MarkCompletedAsync(exportId, ownerToken, storageKey, fileSizeBytes, cancellationToken);
        public Task<bool> MarkFailedDueToMissingDocumentsAsync(Guid exportId, Guid ownerToken, int missingCount, CancellationToken cancellationToken)
        {
            MissingDocsMarkFailedCount++;
            return denyMissingDocsMarkFailed
                ? Task.FromResult(false)
                : inner.MarkFailedDueToMissingDocumentsAsync(exportId, ownerToken, missingCount, cancellationToken);
        }
        public Task ResetForRetryAsync(Guid exportId, CancellationToken cancellationToken) =>
            inner.ResetForRetryAsync(exportId, cancellationToken);
        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetRecoverableAsync(
            DateTimeOffset pendingQueuedBefore, DateTimeOffset leaseExpiredAsOf, CancellationToken cancellationToken) =>
            inner.GetRecoverableAsync(pendingQueuedBefore, leaseExpiredAsOf, cancellationToken);
        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetExpiredAsync(CancellationToken cancellationToken) =>
            inner.GetExpiredAsync(cancellationToken);
        public Task MarkExpiredAsync(Guid exportId, CancellationToken cancellationToken) =>
            inner.MarkExpiredAsync(exportId, cancellationToken);
        public Task<bool> ClaimForRecoveryAsync(Guid exportId, Guid recoveryToken, CancellationToken cancellationToken) =>
            inner.ClaimForRecoveryAsync(exportId, recoveryToken, cancellationToken);
        public Task<bool> ResetForRetryAsync(Guid exportId, Guid recoveryToken, CancellationToken cancellationToken) =>
            inner.ResetForRetryAsync(exportId, recoveryToken, cancellationToken);
        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetArtefactCleanupCandidatesAsync(int batchSize, CancellationToken cancellationToken) =>
            inner.GetArtefactCleanupCandidatesAsync(batchSize, cancellationToken);
        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetRecentlyCleanedArtefactsAsync(int batchSize, CancellationToken cancellationToken) =>
            inner.GetRecentlyCleanedArtefactsAsync(batchSize, cancellationToken);
        public Task MarkAttemptFilesCleanedAsync(Guid exportId, CancellationToken cancellationToken) =>
            inner.MarkAttemptFilesCleanedAsync(exportId, cancellationToken);
        public Task DeferArtefactCleanupAsync(Guid exportId, CancellationToken cancellationToken) =>
            inner.DeferArtefactCleanupAsync(exportId, cancellationToken);
        public Task RecordLateUploadRecheckAsync(Guid exportId, bool succeeded, CancellationToken cancellationToken) =>
            inner.RecordLateUploadRecheckAsync(exportId, succeeded, cancellationToken);
    }

    private sealed class LeaseDenyingJobStore(
        IOrganisationDataExportJobStore inner, bool denyRenew = false, bool denyComplete = false)
        : IOrganisationDataExportJobStore
    {
        public Task<bool> RenewLeaseAsync(Guid exportId, Guid ownerToken, CancellationToken cancellationToken) =>
            denyRenew ? Task.FromResult(false) : inner.RenewLeaseAsync(exportId, ownerToken, cancellationToken);
        public Task<bool> MarkCompletedAsync(Guid exportId, Guid ownerToken, string storageKey, long fileSizeBytes, CancellationToken cancellationToken) =>
            denyComplete ? Task.FromResult(false) : inner.MarkCompletedAsync(exportId, ownerToken, storageKey, fileSizeBytes, cancellationToken);

        public Task<bool> BeginAttemptAsync(Guid exportId, Guid ownerToken, CancellationToken cancellationToken) =>
            inner.BeginAttemptAsync(exportId, ownerToken, cancellationToken);
        public Task<OrganisationDataExportJobView?> GetAsync(Guid exportId, CancellationToken cancellationToken) =>
            inner.GetAsync(exportId, cancellationToken);
        public Task MarkInProgressAsync(Guid exportId, CancellationToken cancellationToken) =>
            inner.MarkInProgressAsync(exportId, cancellationToken);
        public Task MarkFailedAsync(Guid exportId, string failureReason, CancellationToken cancellationToken) =>
            inner.MarkFailedAsync(exportId, failureReason, cancellationToken);
        public Task<bool> MarkFailedAsync(Guid exportId, Guid ownerToken, string failureReason, CancellationToken cancellationToken) =>
            inner.MarkFailedAsync(exportId, ownerToken, failureReason, cancellationToken);
        public Task<bool> MarkFailedDueToMissingDocumentsAsync(Guid exportId, Guid ownerToken, int missingCount, CancellationToken cancellationToken) =>
            inner.MarkFailedDueToMissingDocumentsAsync(exportId, ownerToken, missingCount, cancellationToken);
        public Task ResetForRetryAsync(Guid exportId, CancellationToken cancellationToken) =>
            inner.ResetForRetryAsync(exportId, cancellationToken);
        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetRecoverableAsync(
            DateTimeOffset pendingQueuedBefore, DateTimeOffset leaseExpiredAsOf, CancellationToken cancellationToken) =>
            inner.GetRecoverableAsync(pendingQueuedBefore, leaseExpiredAsOf, cancellationToken);
        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetExpiredAsync(CancellationToken cancellationToken) =>
            inner.GetExpiredAsync(cancellationToken);
        public Task MarkExpiredAsync(Guid exportId, CancellationToken cancellationToken) =>
            inner.MarkExpiredAsync(exportId, cancellationToken);
        public Task<bool> ClaimForRecoveryAsync(Guid exportId, Guid recoveryToken, CancellationToken cancellationToken) =>
            inner.ClaimForRecoveryAsync(exportId, recoveryToken, cancellationToken);
        public Task<bool> ResetForRetryAsync(Guid exportId, Guid recoveryToken, CancellationToken cancellationToken) =>
            inner.ResetForRetryAsync(exportId, recoveryToken, cancellationToken);
        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetArtefactCleanupCandidatesAsync(int batchSize, CancellationToken cancellationToken) =>
            inner.GetArtefactCleanupCandidatesAsync(batchSize, cancellationToken);
        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetRecentlyCleanedArtefactsAsync(int batchSize, CancellationToken cancellationToken) =>
            inner.GetRecentlyCleanedArtefactsAsync(batchSize, cancellationToken);
        public Task MarkAttemptFilesCleanedAsync(Guid exportId, CancellationToken cancellationToken) =>
            inner.MarkAttemptFilesCleanedAsync(exportId, cancellationToken);
        public Task DeferArtefactCleanupAsync(Guid exportId, CancellationToken cancellationToken) =>
            inner.DeferArtefactCleanupAsync(exportId, cancellationToken);
        public Task RecordLateUploadRecheckAsync(Guid exportId, bool succeeded, CancellationToken cancellationToken) =>
            inner.RecordLateUploadRecheckAsync(exportId, succeeded, cancellationToken);
    }

    /// <summary>
    /// Serialises every call to the inner store behind a single gate. Models the production separation
    /// of the worker's DbContext scope from the renewal loop's, which EF Core's in-memory provider
    /// (no concurrent operations on one context) does not tolerate when both share one context here.
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

    private sealed class RecordingIntegrationEventPublisher : IIntegrationEventPublisher
    {
        public List<object> Published { get; } = [];

        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
            where TEvent : IIntegrationEvent
        {
            Published.Add(integrationEvent!);
            return Task.CompletedTask;
        }
    }

    private sealed class ClaimRefusingJobStore(IOrganisationDataExportJobStore inner) : IOrganisationDataExportJobStore
    {
        public Task<bool> BeginAttemptAsync(Guid exportId, Guid ownerToken, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<OrganisationDataExportJobView?> GetAsync(Guid exportId, CancellationToken cancellationToken) =>
            inner.GetAsync(exportId, cancellationToken);
        public Task MarkInProgressAsync(Guid exportId, CancellationToken cancellationToken) =>
            inner.MarkInProgressAsync(exportId, cancellationToken);
        public Task<bool> RenewLeaseAsync(Guid exportId, Guid ownerToken, CancellationToken cancellationToken) =>
            inner.RenewLeaseAsync(exportId, ownerToken, cancellationToken);
        public Task<bool> MarkCompletedAsync(Guid exportId, Guid ownerToken, string storageKey, long fileSizeBytes, CancellationToken cancellationToken) =>
            inner.MarkCompletedAsync(exportId, ownerToken, storageKey, fileSizeBytes, cancellationToken);
        public Task MarkFailedAsync(Guid exportId, string failureReason, CancellationToken cancellationToken) =>
            inner.MarkFailedAsync(exportId, failureReason, cancellationToken);
        public Task<bool> MarkFailedAsync(Guid exportId, Guid ownerToken, string failureReason, CancellationToken cancellationToken) =>
            inner.MarkFailedAsync(exportId, ownerToken, failureReason, cancellationToken);
        public Task<bool> MarkFailedDueToMissingDocumentsAsync(Guid exportId, Guid ownerToken, int missingCount, CancellationToken cancellationToken) =>
            inner.MarkFailedDueToMissingDocumentsAsync(exportId, ownerToken, missingCount, cancellationToken);
        public Task ResetForRetryAsync(Guid exportId, CancellationToken cancellationToken) =>
            inner.ResetForRetryAsync(exportId, cancellationToken);
        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetRecoverableAsync(
            DateTimeOffset pendingQueuedBefore, DateTimeOffset leaseExpiredAsOf, CancellationToken cancellationToken) =>
            inner.GetRecoverableAsync(pendingQueuedBefore, leaseExpiredAsOf, cancellationToken);
        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetExpiredAsync(CancellationToken cancellationToken) =>
            inner.GetExpiredAsync(cancellationToken);
        public Task MarkExpiredAsync(Guid exportId, CancellationToken cancellationToken) =>
            inner.MarkExpiredAsync(exportId, cancellationToken);
        public Task<bool> ClaimForRecoveryAsync(Guid exportId, Guid recoveryToken, CancellationToken cancellationToken) =>
            inner.ClaimForRecoveryAsync(exportId, recoveryToken, cancellationToken);
        public Task<bool> ResetForRetryAsync(Guid exportId, Guid recoveryToken, CancellationToken cancellationToken) =>
            inner.ResetForRetryAsync(exportId, recoveryToken, cancellationToken);
        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetArtefactCleanupCandidatesAsync(int batchSize, CancellationToken cancellationToken) =>
            inner.GetArtefactCleanupCandidatesAsync(batchSize, cancellationToken);
        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetRecentlyCleanedArtefactsAsync(int batchSize, CancellationToken cancellationToken) =>
            inner.GetRecentlyCleanedArtefactsAsync(batchSize, cancellationToken);
        public Task MarkAttemptFilesCleanedAsync(Guid exportId, CancellationToken cancellationToken) =>
            inner.MarkAttemptFilesCleanedAsync(exportId, cancellationToken);
        public Task DeferArtefactCleanupAsync(Guid exportId, CancellationToken cancellationToken) =>
            inner.DeferArtefactCleanupAsync(exportId, cancellationToken);
        public Task RecordLateUploadRecheckAsync(Guid exportId, bool succeeded, CancellationToken cancellationToken) =>
            inner.RecordLateUploadRecheckAsync(exportId, succeeded, cancellationToken);
    }
}
