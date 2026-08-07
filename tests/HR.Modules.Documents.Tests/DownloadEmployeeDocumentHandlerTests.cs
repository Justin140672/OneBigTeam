using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.DownloadEmployeeDocument;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class DownloadEmployeeDocumentHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 18, 10, 0, 0, DateTimeKind.Utc);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static DownloadEmployeeDocumentHandler BuildHandler(
        DocumentsDbContext db,
        FakeDocumentStorageService? storage = null,
        FakeAuditPublisher? auditPublisher = null) =>
        new(db,
            storage ?? new FakeDocumentStorageService(),
            new FakeClock(FixedUtcNow),
            auditPublisher ?? new FakeAuditPublisher());

    private static async Task<(DocumentType docType, Document doc, EmployeeDocument empDoc)> Seed(
        DocumentsDbContext db,
        Guid companyId,
        Guid employeeId)
    {
        var docType = DocumentType.Create(Guid.NewGuid(), companyId, "Contract", null, DateTimeOffset.UtcNow);
        db.DocumentTypes.Add(docType);

        var doc = Document.Create(
            Guid.NewGuid(), companyId, employeeId, "Employment Contract", null,
            docType.Id, "contract.pdf", 1024, "application/pdf",
            $"{companyId}/{employeeId}/abc/contract.pdf",
            null, Guid.NewGuid(), DateTimeOffset.UtcNow);
        // Default download-success fixtures assume a clean scan — tests that specifically exercise
        // ScanStatusAccessGuard construct their own Document with a different ScanStatus.
        doc.MarkScanClean(DateTimeOffset.UtcNow);
        db.Documents.Add(doc);

        var empDoc = EmployeeDocument.Create(
            Guid.NewGuid(), companyId, employeeId, doc.Id, Guid.NewGuid(), DateTimeOffset.UtcNow);
        db.EmployeeDocuments.Add(empDoc);

        await db.SaveChangesAsync();
        return (docType, doc, empDoc);
    }

    [Fact]
    public async Task HandleAsync_Returns_DownloadUrl_Derived_From_StorageKey()
    {
        await using var db        = BuildContext();
        var storage               = new FakeDocumentStorageService();
        var companyId             = Guid.NewGuid();
        var employeeId            = Guid.NewGuid();
        var (_, doc, empDoc)      = await Seed(db, companyId, employeeId);
        var handler               = BuildHandler(db, storage);

        var result = await handler.HandleAsync(
            new DownloadEmployeeDocumentRequest
            {
                CompanyId          = companyId,
                EmployeeId         = employeeId,
                EmployeeDocumentId = empDoc.Id,
            },
            downloadedBy: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(doc.StorageKey, result.Value!.ToString());
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_EmployeeDocumentId_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            new DownloadEmployeeDocumentRequest
            {
                CompanyId          = Guid.NewGuid(),
                EmployeeId         = Guid.NewGuid(),
                EmployeeDocumentId = Guid.NewGuid(),
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_CompanyId_Does_Not_Match()
    {
        await using var db    = BuildContext();
        var companyId         = Guid.NewGuid();
        var employeeId        = Guid.NewGuid();
        var (_, _, empDoc)    = await Seed(db, companyId, employeeId);
        var handler           = BuildHandler(db);

        var result = await handler.HandleAsync(
            new DownloadEmployeeDocumentRequest
            {
                CompanyId          = Guid.NewGuid(),
                EmployeeId         = employeeId,
                EmployeeDocumentId = empDoc.Id,
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_EmployeeId_Does_Not_Match()
    {
        await using var db    = BuildContext();
        var companyId         = Guid.NewGuid();
        var employeeId        = Guid.NewGuid();
        var (_, _, empDoc)    = await Seed(db, companyId, employeeId);
        var handler           = BuildHandler(db);

        var result = await handler.HandleAsync(
            new DownloadEmployeeDocumentRequest
            {
                CompanyId          = companyId,
                EmployeeId         = Guid.NewGuid(),
                EmployeeDocumentId = empDoc.Id,
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Publishes_DocumentDownloaded_Audit_Event()
    {
        await using var db          = BuildContext();
        var audit                   = new FakeAuditPublisher();
        var companyId               = Guid.NewGuid();
        var employeeId              = Guid.NewGuid();
        var downloadedBy            = Guid.NewGuid();
        var (docType, _, empDoc)    = await Seed(db, companyId, employeeId);
        var handler                 = BuildHandler(db, auditPublisher: audit);

        await handler.HandleAsync(
            new DownloadEmployeeDocumentRequest
            {
                CompanyId          = companyId,
                EmployeeId         = employeeId,
                EmployeeDocumentId = empDoc.Id,
            },
            downloadedBy,
            CancellationToken.None);

        var evt = Assert.Single(audit.Published);
        Assert.Equal("document.downloaded", evt.EventType);
        Assert.Equal("EmployeeDocument",    evt.EntityType);
        Assert.Equal(empDoc.Id,             evt.EntityId);
        Assert.Equal(companyId,             evt.CompanyId);
        Assert.Equal(downloadedBy,          evt.ActorUserId);
        Assert.Null(evt.ActorEmployeeId);
        Assert.Contains("Employment Contract", evt.Summary);
        Assert.Null(evt.Before);
        Assert.Null(evt.After);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_When_Document_Not_Found()
    {
        await using var db = BuildContext();
        var audit          = new FakeAuditPublisher();
        var handler        = BuildHandler(db, auditPublisher: audit);

        await handler.HandleAsync(
            new DownloadEmployeeDocumentRequest
            {
                CompanyId          = Guid.NewGuid(),
                EmployeeId         = Guid.NewGuid(),
                EmployeeDocumentId = Guid.NewGuid(),
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Empty(audit.Published);
    }

    // Theory parameters must be a publicly accessible type (xUnit requires public test methods),
    // but FileScanStatus is internal — pass the enum's underlying int value instead and cast.
    [Theory]
    [InlineData((int)FileScanStatus.Pending, "This document is currently being security checked.")]
    [InlineData((int)FileScanStatus.Scanning, "This document is currently being security checked.")]
    [InlineData((int)FileScanStatus.Infected, "This document failed a security scan.")]
    [InlineData((int)FileScanStatus.Failed, "This document failed a security scan.")]
    public async Task HandleAsync_Returns_Validation_When_Document_Is_Not_Clean(
        int statusValue, string expectedMessage)
    {
        var status = (FileScanStatus)statusValue;
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var (_, _, empDoc)  = await SeedWithScanStatus(db, companyId, employeeId, status);
        var handler          = BuildHandler(db);

        var result = await handler.HandleAsync(
            new DownloadEmployeeDocumentRequest
            {
                CompanyId          = companyId,
                EmployeeId         = employeeId,
                EmployeeDocumentId = empDoc.Id,
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Equal(expectedMessage, result.Error.Message);
    }

    // Document.Create defaults to Pending, so the only status that needs an explicit Mark* call
    // is anything other than Pending.
    private static async Task<(DocumentType docType, Document doc, EmployeeDocument empDoc)> SeedWithScanStatus(
        DocumentsDbContext db,
        Guid companyId,
        Guid employeeId,
        FileScanStatus status)
    {
        var docType = DocumentType.Create(Guid.NewGuid(), companyId, "Contract", null, DateTimeOffset.UtcNow);
        db.DocumentTypes.Add(docType);

        var doc = Document.Create(
            Guid.NewGuid(), companyId, employeeId, "Employment Contract", null,
            docType.Id, "contract.pdf", 1024, "application/pdf",
            $"{companyId}/{employeeId}/abc/contract.pdf",
            null, Guid.NewGuid(), DateTimeOffset.UtcNow);

        switch (status)
        {
            case FileScanStatus.Pending:
                break; // already Pending by default
            case FileScanStatus.Scanning:
                doc.MarkScanning(DateTimeOffset.UtcNow);
                break;
            case FileScanStatus.Infected:
                doc.MarkScanInfected("EICAR.Test.File", DateTimeOffset.UtcNow);
                break;
            case FileScanStatus.Failed:
                doc.MarkScanFailed("scanner unreachable", DateTimeOffset.UtcNow);
                break;
        }
        db.Documents.Add(doc);

        var empDoc = EmployeeDocument.Create(
            Guid.NewGuid(), companyId, employeeId, doc.Id, Guid.NewGuid(), DateTimeOffset.UtcNow);
        db.EmployeeDocuments.Add(empDoc);

        await db.SaveChangesAsync();
        return (docType, doc, empDoc);
    }
}
