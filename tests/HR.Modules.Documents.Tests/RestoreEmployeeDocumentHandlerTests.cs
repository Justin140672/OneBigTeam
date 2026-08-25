using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.RestoreEmployeeDocument;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class RestoreEmployeeDocumentHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 18, 10, 0, 0, DateTimeKind.Utc);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static RestoreEmployeeDocumentHandler BuildHandler(
        DocumentsDbContext db,
        FakeAuditPublisher? auditPublisher = null) =>
        new(db,
            new FakeClock(FixedUtcNow),
            auditPublisher ?? new FakeAuditPublisher());

    private static async Task<(DocumentType docType, Document doc, EmployeeDocument empDoc)> Seed(
        DocumentsDbContext db,
        Guid companyId,
        Guid employeeId,
        bool archived = true)
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
            Guid.NewGuid(), companyId, employeeId, doc.Id, Guid.NewGuid(), DateTimeOffset.UtcNow);
        if (archived)
            empDoc.Archive(Guid.NewGuid(), "No longer needed", DateTimeOffset.UtcNow.AddDays(-1));
        db.EmployeeDocuments.Add(empDoc);

        await db.SaveChangesAsync();
        return (docType, doc, empDoc);
    }

    private static RestoreEmployeeDocumentRequest BuildRequest(
        Guid companyId, Guid employeeId, Guid employeeDocumentId) =>
        new()
        {
            CompanyId          = companyId,
            EmployeeId         = employeeId,
            EmployeeDocumentId = employeeDocumentId,
        };

    [Fact]
    public async Task HandleAsync_Restores_Archived_Document()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var (_, _, empDoc)  = await Seed(db, companyId, employeeId);
        var handler         = BuildHandler(db);
        var restoredBy      = Guid.NewGuid();

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employeeId, empDoc.Id),
            restoredBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(empDoc.Id, result.Value!.EmployeeDocumentId);
        Assert.Equal(companyId, result.Value.CompanyId);
        Assert.Equal(restoredBy, result.Value.RestoredByUserId);
        Assert.Equal(FixedUtcNow, result.Value.RestoredAt.UtcDateTime);

        var stored = await db.EmployeeDocuments.SingleAsync(ed => ed.Id == empDoc.Id);
        Assert.False(stored.IsArchived);
        Assert.Null(stored.ArchivedByUserId);
        Assert.Null(stored.ArchivedAt);
        Assert.Null(stored.ArchiveReason);
        Assert.Equal(restoredBy, stored.RestoredByUserId);
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
        await using var db = BuildContext();
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
        await using var db = BuildContext();
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
    public async Task HandleAsync_Returns_Conflict_When_Document_Is_Not_Archived()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var (_, _, empDoc)  = await Seed(db, companyId, employeeId, archived: false);
        var handler         = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employeeId, empDoc.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Publishes_EmployeeDocumentRestored_Audit_Event()
    {
        await using var db     = BuildContext();
        var audit               = new FakeAuditPublisher();
        var companyId           = Guid.NewGuid();
        var employeeId          = Guid.NewGuid();
        var restoredBy          = Guid.NewGuid();
        var (_, _, empDoc)      = await Seed(db, companyId, employeeId);
        var handler              = BuildHandler(db, audit);

        await handler.HandleAsync(
            BuildRequest(companyId, employeeId, empDoc.Id),
            restoredBy,
            CancellationToken.None);

        var evt = Assert.Single(audit.Published);
        Assert.Equal("employee_document.restored", evt.EventType);
        Assert.Equal("EmployeeDocument",           evt.EntityType);
        Assert.Equal(empDoc.Id,                    evt.EntityId);
        Assert.Equal(companyId,                    evt.CompanyId);
        Assert.Equal(restoredBy,                   evt.ActorUserId);
        Assert.Null(evt.ActorEmployeeId);
        Assert.Contains("Employment Contract", evt.Summary);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_When_Document_Not_Found()
    {
        await using var db = BuildContext();
        var audit           = new FakeAuditPublisher();
        var handler          = BuildHandler(db, audit);

        await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Empty(audit.Published);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_When_Not_Archived()
    {
        await using var db = BuildContext();
        var audit           = new FakeAuditPublisher();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var (_, _, empDoc)  = await Seed(db, companyId, employeeId, archived: false);
        var handler          = BuildHandler(db, audit);

        await handler.HandleAsync(
            BuildRequest(companyId, employeeId, empDoc.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Empty(audit.Published);
    }
}
