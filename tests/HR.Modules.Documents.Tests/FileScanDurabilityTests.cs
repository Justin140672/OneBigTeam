using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using HR.Modules.Documents;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Jobs;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HR.Modules.Documents.Tests;

public class FileScanDurabilityTests
{
    private static readonly DateTime T0 = new(2026, 8, 6, 10, 0, 0, DateTimeKind.Utc);

    private static DbContextOptions<DocumentsDbContext> NewDatabase() =>
        new DbContextOptionsBuilder<DocumentsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;

    private sealed class Rig
    {
        public required DbContextOptions<DocumentsDbContext> Options { get; init; }
        public FakeVirusScanService Scanner { get; } = new();
        public FakeAuditPublisher Audit { get; } = new();
        public FakeDocumentStorageService DocumentStorage { get; } = new();
        public FakeProfilePhotoStorageService PhotoStorage { get; } = new();
        public StubHttpMessageHandler Http { get; } = new();
        public FakeLogger<ScanUploadedFileJob> Logger { get; } = new();

        public DocumentsDbContext Open() => new(Options);

        public ScanUploadedFileJob Job(DocumentsDbContext db, DateTime now) =>
            new(db, DocumentStorage, PhotoStorage, Scanner, new FakeHttpClientFactory(Http), new FakeClock(now), Audit, Logger);
    }

    private static Rig NewRig() => new() { Options = NewDatabase() };

    private static async Task<(Document Doc, FileScanWork Work)> SeedDocumentWithWorkAsync(Rig rig, DateTime now)
    {
        await using var db = rig.Open();
        var companyId = Guid.NewGuid();
        var docType = DocumentType.Create(Guid.NewGuid(), companyId, "Contract", null, now);
        db.DocumentTypes.Add(docType);
        var doc = Document.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), "Contract", null, docType.Id, "c.pdf", 10, "application/pdf",
            $"{companyId}/c.pdf", null, Guid.NewGuid(), now);
        db.Documents.Add(doc);
        var work = FileScanWork.Create(FileScanTargetType.Document, doc.Id, companyId, now);
        db.FileScanWork.Add(work);
        await db.SaveChangesAsync();
        return (doc, work);
    }

    [Fact]
    public async Task Duplicate_Dispatch_While_The_Lease_Is_Active_Does_Not_Scan_Twice()
    {
        var rig = NewRig();
        var (doc, work) = await SeedDocumentWithWorkAsync(rig, T0);
        var scans = 0;
        rig.Scanner.OnScan = () =>
        {
            scans++;
            if (scans == 1)
            {
                using var other = rig.Open();
                rig.Job(other, T0.AddSeconds(5)).ExecuteAsync(FileScanTargetType.Document, doc.Id, doc.CompanyId).GetAwaiter().GetResult();
            }
        };

        await using var db = rig.Open();
        await rig.Job(db, T0).ExecuteAsync(FileScanTargetType.Document, doc.Id, doc.CompanyId);

        Assert.Equal(1, scans);
        await using var verify = rig.Open();
        Assert.Equal(FileScanStatus.Clean, (await verify.Documents.SingleAsync(d => d.Id == doc.Id)).ScanStatus);
        Assert.Equal(FileScanWorkState.Completed, (await verify.FileScanWork.SingleAsync(w => w.Id == work.Id)).State);
        Assert.Single(rig.Audit.Published);
    }

    [Fact]
    public async Task Late_Duplicate_Job_After_Completion_Cannot_Change_The_Result()
    {
        var rig = NewRig();
        var (doc, _) = await SeedDocumentWithWorkAsync(rig, T0);
        rig.Scanner.ReturnInfected = true;
        await using (var db = rig.Open())
        {
            await rig.Job(db, T0).ExecuteAsync(FileScanTargetType.Document, doc.Id, doc.CompanyId);
        }

        rig.Scanner.ReturnInfected = false;
        await using (var db = rig.Open())
        {
            await rig.Job(db, T0.AddHours(1)).ExecuteAsync(FileScanTargetType.Document, doc.Id, doc.CompanyId);
        }

        await using var verify = rig.Open();
        Assert.Equal(FileScanStatus.Infected, (await verify.Documents.SingleAsync(d => d.Id == doc.Id)).ScanStatus);
    }

    [Fact]
    public async Task Job_Whose_Lease_Was_Taken_Over_Discards_Its_Result()
    {
        var rig = NewRig();
        var (doc, work) = await SeedDocumentWithWorkAsync(rig, T0);
        rig.Scanner.ReturnInfected = true;
        rig.Scanner.OnScan = () =>
        {
            using var other = rig.Open();
            var row = other.FileScanWork.Single(w => w.Id == work.Id);
            row.ReleaseForImmediateRetry("stale_claim", T0.AddMinutes(1));
            row.Claim(T0.AddMinutes(1));
            other.SaveChanges();
        };

        await using (var db = rig.Open())
        {
            await rig.Job(db, T0).ExecuteAsync(FileScanTargetType.Document, doc.Id, doc.CompanyId);
        }

        await using var verify = rig.Open();
        var stored = await verify.Documents.SingleAsync(d => d.Id == doc.Id);
        Assert.Equal(FileScanStatus.Scanning, stored.ScanStatus);
        Assert.Empty(rig.Audit.Published);
        Assert.Equal(FileScanWorkState.Scanning, (await verify.FileScanWork.SingleAsync(w => w.Id == work.Id)).State);
    }

    [Fact]
    public async Task Stale_Scanning_Claim_Is_Reclaimed_By_A_Later_Job_After_The_Lease_Expires()
    {
        var rig = NewRig();
        var (doc, work) = await SeedDocumentWithWorkAsync(rig, T0);
        await using (var db = rig.Open())
        {
            var row = await db.FileScanWork.SingleAsync(w => w.Id == work.Id);
            row.Claim(T0);
            await db.SaveChangesAsync();
        }

        await using (var db = rig.Open())
        {
            await rig.Job(db, T0.AddMinutes(1)).ExecuteAsync(FileScanTargetType.Document, doc.Id, doc.CompanyId);
        }

        await using var stillLeased = rig.Open();
        Assert.Equal(FileScanStatus.Pending, (await stillLeased.Documents.SingleAsync(d => d.Id == doc.Id)).ScanStatus);

        await using (var db = rig.Open())
        {
            await rig.Job(db, T0.AddMinutes(11)).ExecuteAsync(FileScanTargetType.Document, doc.Id, doc.CompanyId);
        }

        await using var verify = rig.Open();
        Assert.Equal(FileScanStatus.Clean, (await verify.Documents.SingleAsync(d => d.Id == doc.Id)).ScanStatus);
    }

    [Fact]
    public async Task Retry_Exhaustion_Moves_The_File_To_The_Safe_Failed_State_And_Never_Clean()
    {
        var rig = NewRig();
        var (doc, work) = await SeedDocumentWithWorkAsync(rig, T0);
        rig.Http.ThrowException = new InvalidOperationException("scanner unreachable");

        for (var attempt = 1; attempt <= FileScanWork.MaxAttempts; attempt++)
        {
            await using var db = rig.Open();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                rig.Job(db, T0.AddHours(attempt)).ExecuteAsync(FileScanTargetType.Document, doc.Id, doc.CompanyId));
        }

        await using var verify = rig.Open();
        var stored = await verify.Documents.SingleAsync(d => d.Id == doc.Id);
        Assert.Equal(FileScanStatus.Failed, stored.ScanStatus);
        var row = await verify.FileScanWork.SingleAsync(w => w.Id == work.Id);
        Assert.Equal(FileScanWorkState.Exhausted, row.State);
        Assert.Equal(FileScanWork.MaxAttempts, row.AttemptCount);
        Assert.Contains(rig.Audit.Published.OfType<FileScanStatusChangedAuditEvent>(), e => e.NewStatus == "Failed");

        rig.Http.ThrowException = null;
        await using (var db = rig.Open())
        {
            await rig.Job(db, T0.AddDays(1)).ExecuteAsync(FileScanTargetType.Document, doc.Id, doc.CompanyId);
        }

        await using var after = rig.Open();
        Assert.Equal(FileScanStatus.Failed, (await after.Documents.SingleAsync(d => d.Id == doc.Id)).ScanStatus);
    }

    [Fact]
    public async Task Infected_Or_Failed_Files_Cannot_Be_Marked_Clean_By_A_Racing_Job()
    {
        var doc = Document.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "t", null, Guid.NewGuid(), "f.pdf", 1, "application/pdf",
            "k", null, Guid.NewGuid(), DateTimeOffset.UtcNow);
        doc.MarkScanInfected("EICAR", DateTimeOffset.UtcNow);
        doc.MarkScanClean(DateTimeOffset.UtcNow);
        Assert.Equal(FileScanStatus.Infected, doc.ScanStatus);

        var failed = Document.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "t", null, Guid.NewGuid(), "f.pdf", 1, "application/pdf",
            "k", null, Guid.NewGuid(), DateTimeOffset.UtcNow);
        failed.MarkScanFailed("x", DateTimeOffset.UtcNow);
        failed.MarkScanClean(DateTimeOffset.UtcNow);
        Assert.Equal(FileScanStatus.Failed, failed.ScanStatus);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Reconciler_Redispatches_Overdue_Pending_Work_For_Every_Target_Type(int targetTypeValue)
    {
        var targetType = (FileScanTargetType)targetTypeValue;
        var options = NewDatabase();
        var companyId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        await using (var db = new DocumentsDbContext(options))
        {
            db.FileScanWork.Add(FileScanWork.Create(targetType, entityId, companyId, new DateTimeOffset(T0, TimeSpan.Zero)));
            await db.SaveChangesAsync();
        }

        var jobs = new SpyBackgroundJobClient();
        await using var reconcileDb = new DocumentsDbContext(options);
        var job = BuildReconciler(reconcileDb, jobs, T0.AddMinutes(5), new FakeAuditPublisher());

        await job.ExecuteAsync();

        var created = Assert.Single(jobs.CreatedJobs);
        Assert.Equal(typeof(ScanUploadedFileJob), created.Type);
        Assert.Equal(targetType, created.Args[0]);
        Assert.Equal(entityId, created.Args[1]);

        await using var verify = new DocumentsDbContext(options);
        var row = await verify.FileScanWork.SingleAsync();
        Assert.Equal(1, row.DispatchCount);
        Assert.True(row.NextAttemptAt > new DateTimeOffset(T0.AddMinutes(5), TimeSpan.Zero));
    }

    [Fact]
    public async Task Reconciler_Leaves_Fresh_Pending_Work_Alone_And_Does_Not_Redispatch_Twice()
    {
        var options = NewDatabase();
        await using (var db = new DocumentsDbContext(options))
        {
            db.FileScanWork.Add(FileScanWork.Create(FileScanTargetType.Document, Guid.NewGuid(), Guid.NewGuid(), new DateTimeOffset(T0, TimeSpan.Zero)));
            await db.SaveChangesAsync();
        }

        var jobs = new SpyBackgroundJobClient();
        await using (var db = new DocumentsDbContext(options))
        {
            await BuildReconciler(db, jobs, T0.AddSeconds(30), new FakeAuditPublisher()).ExecuteAsync();
        }

        Assert.Empty(jobs.CreatedJobs);

        await using (var db = new DocumentsDbContext(options))
        {
            await BuildReconciler(db, jobs, T0.AddMinutes(5), new FakeAuditPublisher()).ExecuteAsync();
        }

        await using (var db = new DocumentsDbContext(options))
        {
            await BuildReconciler(db, jobs, T0.AddMinutes(6), new FakeAuditPublisher()).ExecuteAsync();
        }

        Assert.Single(jobs.CreatedJobs);
    }

    [Fact]
    public async Task Reconciler_Releases_Stale_Claims_And_Redispatches_Them_On_The_Next_Run()
    {
        var options = NewDatabase();
        await using (var db = new DocumentsDbContext(options))
        {
            var work = FileScanWork.Create(FileScanTargetType.Document, Guid.NewGuid(), Guid.NewGuid(), new DateTimeOffset(T0, TimeSpan.Zero));
            work.Claim(new DateTimeOffset(T0, TimeSpan.Zero));
            db.FileScanWork.Add(work);
            await db.SaveChangesAsync();
        }

        var jobs = new SpyBackgroundJobClient();
        var audit = new FakeAuditPublisher();
        await using (var db = new DocumentsDbContext(options))
        {
            await BuildReconciler(db, jobs, T0.AddMinutes(11), audit).ExecuteAsync();
        }

        await using (var db = new DocumentsDbContext(options))
        {
            var row = await db.FileScanWork.SingleAsync();
            Assert.Equal(FileScanWorkState.Pending, row.State);
            Assert.Null(row.LeaseToken);
        }

        await using (var db = new DocumentsDbContext(options))
        {
            await BuildReconciler(db, jobs, T0.AddMinutes(14), audit).ExecuteAsync();
        }

        Assert.Single(jobs.CreatedJobs);
        Assert.Contains(audit.Published.OfType<FileScanRecoveredAuditEvent>(), e => e.Action == "stale_claim_released");
    }

    [Fact]
    public async Task Reconciler_Moves_Work_That_Used_All_Attempts_Into_Failed_State()
    {
        var options = NewDatabase();
        var companyId = Guid.NewGuid();
        Guid docId;
        await using (var db = new DocumentsDbContext(options))
        {
            var docType = DocumentType.Create(Guid.NewGuid(), companyId, "C", null, T0);
            db.DocumentTypes.Add(docType);
            var doc = Document.Create(
                Guid.NewGuid(), companyId, Guid.NewGuid(), "t", null, docType.Id, "f.pdf", 1, "application/pdf",
                "k", null, Guid.NewGuid(), T0);
            doc.MarkScanning(T0);
            db.Documents.Add(doc);
            docId = doc.Id;
            var work = FileScanWork.Create(FileScanTargetType.Document, doc.Id, companyId, new DateTimeOffset(T0, TimeSpan.Zero));
            for (var i = 0; i < FileScanWork.MaxAttempts; i++)
            {
                work.Claim(new DateTimeOffset(T0, TimeSpan.Zero));
            }

            db.FileScanWork.Add(work);
            await db.SaveChangesAsync();
        }

        var audit = new FakeAuditPublisher();
        await using (var db = new DocumentsDbContext(options))
        {
            await BuildReconciler(db, new SpyBackgroundJobClient(), T0.AddHours(1), audit).ExecuteAsync();
        }

        await using var verify = new DocumentsDbContext(options);
        Assert.Equal(FileScanStatus.Failed, (await verify.Documents.SingleAsync(d => d.Id == docId)).ScanStatus);
        Assert.Equal(FileScanWorkState.Exhausted, (await verify.FileScanWork.SingleAsync()).State);
        Assert.Contains(audit.Published.OfType<FileScanStatusChangedAuditEvent>(), e => e.NewStatus == "Failed");
    }

    [Fact]
    public async Task Reconciler_Enqueue_Failure_Leaves_The_Work_Due_For_The_Next_Run()
    {
        var options = NewDatabase();
        await using (var db = new DocumentsDbContext(options))
        {
            db.FileScanWork.Add(FileScanWork.Create(FileScanTargetType.Document, Guid.NewGuid(), Guid.NewGuid(), new DateTimeOffset(T0, TimeSpan.Zero)));
            await db.SaveChangesAsync();
        }

        await using (var db = new DocumentsDbContext(options))
        {
            await BuildReconciler(db, new ThrowingBackgroundJobClient(), T0.AddMinutes(5), new FakeAuditPublisher()).ExecuteAsync();
        }

        var jobs = new SpyBackgroundJobClient();
        await using (var db = new DocumentsDbContext(options))
        {
            await BuildReconciler(db, jobs, T0.AddMinutes(6), new FakeAuditPublisher()).ExecuteAsync();
        }

        Assert.Single(jobs.CreatedJobs);
    }

    [Fact]
    public async Task Reconciler_Processes_At_Most_One_Batch_Per_Run()
    {
        var options = NewDatabase();
        await using (var db = new DocumentsDbContext(options))
        {
            for (var i = 0; i < ReconcileFileScansJob.BatchSize + 20; i++)
            {
                db.FileScanWork.Add(FileScanWork.Create(FileScanTargetType.Document, Guid.NewGuid(), Guid.NewGuid(), new DateTimeOffset(T0, TimeSpan.Zero)));
            }

            await db.SaveChangesAsync();
        }

        var jobs = new SpyBackgroundJobClient();
        await using var run = new DocumentsDbContext(options);
        await BuildReconciler(run, jobs, T0.AddMinutes(5), new FakeAuditPublisher()).ExecuteAsync();

        Assert.Equal(ReconcileFileScansJob.BatchSize, jobs.CreatedJobs.Count);
    }

    [Fact]
    public async Task Backlog_Reader_And_Health_Check_Expose_Pending_Age_Stale_Claims_Retries_And_Exhaustion()
    {
        var options = NewDatabase();
        var created = new DateTimeOffset(T0, TimeSpan.Zero);
        await using (var db = new DocumentsDbContext(options))
        {
            db.FileScanWork.Add(FileScanWork.Create(FileScanTargetType.Document, Guid.NewGuid(), Guid.NewGuid(), created));

            var stale = FileScanWork.Create(FileScanTargetType.Document, Guid.NewGuid(), Guid.NewGuid(), created);
            stale.Claim(created);
            stale.Claim(created);
            db.FileScanWork.Add(stale);

            var exhausted = FileScanWork.Create(FileScanTargetType.Document, Guid.NewGuid(), Guid.NewGuid(), created);
            exhausted.Exhaust("scan_attempts_exhausted", created.AddMinutes(50));
            db.FileScanWork.Add(exhausted);
            await db.SaveChangesAsync();
        }

        await using var read = new DocumentsDbContext(options);
        var reader = new FileScanBacklogReader(read, new FakeClock(T0.AddMinutes(60)));

        var backlog = await reader.ReadAsync(CancellationToken.None);

        Assert.Equal(1, backlog.PendingCount);
        Assert.Equal(TimeSpan.FromMinutes(60).TotalSeconds, backlog.OldestPendingAgeSeconds);
        Assert.Equal(1, backlog.StaleClaimCount);
        Assert.Equal(1, backlog.RetriedCount);
        Assert.Equal(1, backlog.ExhaustedCount);
        Assert.Equal(1, backlog.ExhaustedLast24HoursCount);

        var result = await new FileScanBacklogHealthCheck(reader).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal(1, result.Data["staleClaimCount"]);
        Assert.Equal(1, result.Data["exhaustedCount"]);
    }

    private static ReconcileFileScansJob BuildReconciler(
        DocumentsDbContext db, IBackgroundJobClient jobs, DateTime now, FakeAuditPublisher audit)
    {
        var clock = new FakeClock(now);
        return new ReconcileFileScansJob(
            db, jobs, clock, audit, new FileScanBacklogReader(db, clock), new FakeLogger<ReconcileFileScansJob>());
    }

}
