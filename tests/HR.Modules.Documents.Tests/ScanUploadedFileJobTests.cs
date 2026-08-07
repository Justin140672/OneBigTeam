using HR.Modules.Documents;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Jobs;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

// NOTE: the final-retry-exhausted "mark Failed" branch reads
// context?.GetJobParameter<int?>("RetryCount") from a Hangfire.Server.PerformContext, which has no
// public, test-friendly constructor. Only the context == null path (retryCount defaults to 0, so
// isFinalAttempt is always false with MaxAttempts = 5) is exercised here — see
// HandleAsync_Scanner_Exception_With_Null_Context_Rethrows_Without_Marking_Failed below. The
// final-attempt-marks-Failed branch is not independently unit-tested for that reason.
public class ScanUploadedFileJobTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 8, 6, 10, 0, 0, DateTimeKind.Utc);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static (
        ScanUploadedFileJob Job,
        FakeDocumentStorageService DocumentStorage,
        FakeProfilePhotoStorageService ProfilePhotoStorage,
        FakeVirusScanService VirusScanner,
        StubHttpMessageHandler HttpHandler,
        FakeAuditPublisher Audit,
        FakeLogger<ScanUploadedFileJob> Logger) BuildJob(DocumentsDbContext db)
    {
        var documentStorage     = new FakeDocumentStorageService();
        var profilePhotoStorage = new FakeProfilePhotoStorageService();
        var virusScanner        = new FakeVirusScanService();
        var httpHandler         = new StubHttpMessageHandler();
        var audit                = new FakeAuditPublisher();
        var logger                = new FakeLogger<ScanUploadedFileJob>();

        var job = new ScanUploadedFileJob(
            db,
            documentStorage,
            profilePhotoStorage,
            virusScanner,
            new FakeHttpClientFactory(httpHandler),
            new FakeClock(FixedUtcNow),
            audit,
            logger);

        return (job, documentStorage, profilePhotoStorage, virusScanner, httpHandler, audit, logger);
    }

    private static async Task<(DocumentType, Document)> SeedPendingDocument(DocumentsDbContext db, Guid companyId)
    {
        var docType = DocumentType.Create(Guid.NewGuid(), companyId, "Contract", null, DateTimeOffset.UtcNow);
        db.DocumentTypes.Add(docType);

        var doc = Document.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), "Employment Contract", null,
            docType.Id, "contract.pdf", 1024, "application/pdf",
            $"{companyId}/abc/contract.pdf", null, Guid.NewGuid(), DateTimeOffset.UtcNow);
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        return (docType, doc);
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Without_Throwing_When_Entity_No_Longer_Exists()
    {
        await using var db = BuildContext();
        var (job, _, _, _, _, audit, _) = BuildJob(db);

        await job.ExecuteAsync(FileScanTargetType.Document, Guid.NewGuid(), Guid.NewGuid());

        Assert.Empty(audit.Published);
    }

    [Fact]
    public async Task ExecuteAsync_Clean_Result_Marks_Entity_Clean_And_Publishes_Audit_Event()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var (_, doc)         = await SeedPendingDocument(db, companyId);
        var (job, _, _, virusScanner, _, audit, _) = BuildJob(db);
        virusScanner.ReturnInfected = false;

        await job.ExecuteAsync(FileScanTargetType.Document, doc.Id, companyId);

        var stored = await db.Documents.SingleAsync(d => d.Id == doc.Id);
        Assert.Equal(FileScanStatus.Clean, stored.ScanStatus);
        Assert.NotNull(stored.ScanCompletedAt);

        var evt = Assert.IsType<FileScanStatusChangedAuditEvent>(Assert.Single(audit.Published));
        Assert.Equal(companyId,               evt.CompanyId);
        Assert.Equal(doc.Id,                  evt.FileEntityId);
        Assert.Equal("Document",              evt.EntityTypeName);
        Assert.Equal("Scanning",              evt.PreviousStatus);
        Assert.Equal("Clean",                 evt.NewStatus);
        Assert.Null(evt.FailureReason);
    }

    [Fact]
    public async Task ExecuteAsync_Infected_Result_Marks_Entity_Infected_Deletes_From_Storage_And_Publishes_Audit_Event()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var (_, doc)         = await SeedPendingDocument(db, companyId);
        var (job, documentStorage, _, virusScanner, _, audit, _) = BuildJob(db);
        virusScanner.ReturnInfected = true;
        virusScanner.ThreatName     = "EICAR.Test.File";

        await job.ExecuteAsync(FileScanTargetType.Document, doc.Id, companyId);

        var stored = await db.Documents.SingleAsync(d => d.Id == doc.Id);
        Assert.Equal(FileScanStatus.Infected, stored.ScanStatus);
        Assert.Equal("EICAR.Test.File",       stored.ScanFailureReason);

        Assert.Single(documentStorage.Deletions);
        Assert.Equal(doc.StorageKey, documentStorage.Deletions[0]);

        var evt = Assert.IsType<FileScanStatusChangedAuditEvent>(Assert.Single(audit.Published));
        Assert.Equal("Infected",          evt.NewStatus);
        Assert.Equal("EICAR.Test.File",   evt.FailureReason);
    }

    [Fact]
    public async Task ExecuteAsync_Infected_Result_Still_Publishes_Audit_Event_When_Storage_Delete_Fails()
    {
        // The delete-from-storage step is best effort — an exception there must not prevent the
        // entity from being marked Infected or the audit event from being published.
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var (_, doc)         = await SeedPendingDocument(db, companyId);
        var (job, documentStorage, _, virusScanner, _, audit, _) = BuildJob(db);
        virusScanner.ReturnInfected = true;
        documentStorage.ThrowOnDelete = true;

        await job.ExecuteAsync(FileScanTargetType.Document, doc.Id, companyId);

        var stored = await db.Documents.SingleAsync(d => d.Id == doc.Id);
        Assert.Equal(FileScanStatus.Infected, stored.ScanStatus);
        Assert.Single(audit.Published);
    }

    [Fact]
    public async Task ExecuteAsync_Scanner_Exception_With_Null_Context_Rethrows_Without_Marking_Failed()
    {
        // context defaults to null (no PerformContext supplied), so retryCount defaults to 0 and
        // isFinalAttempt is always false — the entity is left Scanning, not Failed, and the
        // exception propagates so Hangfire's [AutomaticRetry] can retry it.
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var (_, doc)         = await SeedPendingDocument(db, companyId);
        var (job, _, _, _, httpHandler, audit, _) = BuildJob(db);
        httpHandler.ThrowException = new InvalidOperationException("scanner unreachable");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            job.ExecuteAsync(FileScanTargetType.Document, doc.Id, companyId));

        var stored = await db.Documents.SingleAsync(d => d.Id == doc.Id);
        Assert.Equal(FileScanStatus.Scanning, stored.ScanStatus);
        Assert.Empty(audit.Published);
    }

    [Fact]
    public async Task ExecuteAsync_Marks_Entity_Scanning_Before_Downloading()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var (_, doc)         = await SeedPendingDocument(db, companyId);
        var (job, _, _, _, _, _, _) = BuildJob(db);

        await job.ExecuteAsync(FileScanTargetType.Document, doc.Id, companyId);

        var stored = await db.Documents.SingleAsync(d => d.Id == doc.Id);
        Assert.Equal(1, stored.ScanAttemptCount); // MarkScanning increments this once
    }
}
