using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.DeleteEmployeeDocument;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class DeleteEmployeeDocumentHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 18, 10, 0, 0, DateTimeKind.Utc);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static DeleteEmployeeDocumentHandler BuildHandler(
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
        Guid employeeId,
        DateOnly? issueDate = null,
        DateOnly? expiryDate = null)
    {
        var docType = DocumentType.Create(Guid.NewGuid(), companyId, "Contract", null, DateTimeOffset.UtcNow);
        db.DocumentTypes.Add(docType);

        var doc = Document.Create(
            Guid.NewGuid(), companyId, employeeId, "Employment Contract", null,
            docType.Id, "contract.pdf", 1024, "application/pdf",
            $"{companyId}/{employeeId}/abc/contract.pdf",
            null, Guid.NewGuid(), DateTimeOffset.UtcNow);
        db.Documents.Add(doc);

        var empDoc = EmployeeDocument.Create(
            Guid.NewGuid(), companyId, employeeId, doc.Id, Guid.NewGuid(), DateTimeOffset.UtcNow,
            issueDate: issueDate, expiryDate: expiryDate);
        db.EmployeeDocuments.Add(empDoc);

        await db.SaveChangesAsync();
        return (docType, doc, empDoc);
    }

    private static DeleteEmployeeDocumentRequest BuildRequest(
        Guid companyId, Guid employeeId, Guid employeeDocumentId) =>
        new()
        {
            CompanyId          = companyId,
            EmployeeId         = employeeId,
            EmployeeDocumentId = employeeDocumentId,
        };

    [Fact]
    public async Task HandleAsync_Deletes_EmployeeDocument_And_Document()
    {
        await using var db   = BuildContext();
        var companyId        = Guid.NewGuid();
        var employeeId       = Guid.NewGuid();
        var (_, _, empDoc)   = await Seed(db, companyId, employeeId);
        var handler          = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employeeId, empDoc.Id),
            deletedBy: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(await db.EmployeeDocuments.ToListAsync());
        Assert.Empty(await db.Documents.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Deletes_File_From_Storage_When_Last_Link()
    {
        await using var db          = BuildContext();
        var storage                 = new FakeDocumentStorageService();
        var companyId               = Guid.NewGuid();
        var employeeId              = Guid.NewGuid();
        var (_, doc, empDoc)        = await Seed(db, companyId, employeeId);
        var handler                 = BuildHandler(db, storage);

        await handler.HandleAsync(
            BuildRequest(companyId, employeeId, empDoc.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Single(storage.Deletions);
        Assert.Equal(doc.StorageKey, storage.Deletions[0]);
    }

    [Fact]
    public async Task HandleAsync_Keeps_Document_When_Other_Employees_Are_Linked()
    {
        await using var db        = BuildContext();
        var storage               = new FakeDocumentStorageService();
        var companyId             = Guid.NewGuid();
        var employeeA             = Guid.NewGuid();
        var employeeB             = Guid.NewGuid();
        var (_, doc, empDocA)     = await Seed(db, companyId, employeeA);

        var empDocB = EmployeeDocument.Create(
            Guid.NewGuid(), companyId, employeeB, doc.Id, Guid.NewGuid(), DateTimeOffset.UtcNow);
        db.EmployeeDocuments.Add(empDocB);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db, storage);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employeeA, empDocA.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(await db.EmployeeDocuments.Where(ed => ed.Id == empDocA.Id).ToListAsync());
        Assert.Single(await db.Documents.ToListAsync());
        Assert.Empty(storage.Deletions);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_EmployeeDocumentId_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_CompanyId_Does_Not_Match()
    {
        await using var db  = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var (_, _, empDoc)  = await Seed(db, companyId, employeeId);
        var handler         = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), employeeId, empDoc.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_EmployeeId_Does_Not_Match()
    {
        await using var db  = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var (_, _, empDoc)  = await Seed(db, companyId, employeeId);
        var handler         = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, Guid.NewGuid(), empDoc.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Delete_File_When_NotFound()
    {
        await using var db = BuildContext();
        var storage        = new FakeDocumentStorageService();
        var handler        = BuildHandler(db, storage);

        await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Empty(storage.Deletions);
    }

    [Fact]
    public async Task HandleAsync_Publishes_DocumentDeleted_Audit_Event()
    {
        await using var db        = BuildContext();
        var audit                 = new FakeAuditPublisher();
        var companyId             = Guid.NewGuid();
        var employeeId            = Guid.NewGuid();
        var deletedBy             = Guid.NewGuid();
        var issueDate             = new DateOnly(2025, 1, 1);
        var expiryDate            = new DateOnly(2027, 1, 1);
        var (docType, doc, empDoc) = await Seed(db, companyId, employeeId, issueDate, expiryDate);
        var handler               = BuildHandler(db, auditPublisher: audit);

        await handler.HandleAsync(
            BuildRequest(companyId, employeeId, empDoc.Id),
            deletedBy,
            CancellationToken.None);

        var evt = Assert.Single(audit.Published);
        Assert.Equal("document.deleted",  evt.EventType);
        Assert.Equal("EmployeeDocument",  evt.EntityType);
        Assert.Equal(empDoc.Id,           evt.EntityId);
        Assert.Equal(companyId,           evt.CompanyId);
        Assert.Equal(deletedBy,           evt.ActorUserId);
        Assert.Null(evt.ActorEmployeeId);
        Assert.Contains("Employment Contract", evt.Summary);
        Assert.NotNull(evt.Before);
        Assert.Null(evt.After);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_When_Document_Not_Found()
    {
        await using var db = BuildContext();
        var audit          = new FakeAuditPublisher();
        var handler        = BuildHandler(db, auditPublisher: audit);

        await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Empty(audit.Published);
    }
}
