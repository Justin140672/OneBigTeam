using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.PurgeEligibleArchivedEmployeeDocuments;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class PurgeEligibleArchivedEmployeeDocumentsHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 18, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static PurgeEligibleArchivedEmployeeDocumentsHandler BuildHandler(
        DocumentsDbContext db,
        FakeDocumentStorageService? storage = null,
        FakeAuditPublisher? auditPublisher = null) =>
        new(db,
            storage ?? new FakeDocumentStorageService(),
            new FakeClock(FixedUtcNow),
            auditPublisher ?? new FakeAuditPublisher());

    private static async Task<(Document doc, EmployeeDocument empDoc)> SeedArchived(
        DocumentsDbContext db,
        Guid companyId,
        Guid employeeId,
        DateTimeOffset archivedAt,
        string storageKey)
    {
        var docType = DocumentType.Create(Guid.NewGuid(), companyId, "Contract", null, Now);
        db.DocumentTypes.Add(docType);

        var doc = Document.Create(
            Guid.NewGuid(), companyId, employeeId, "Employment Contract", null,
            docType.Id, "contract.pdf", 1024, "application/pdf",
            storageKey, null, Guid.NewGuid(), Now);
        db.Documents.Add(doc);

        var empDoc = EmployeeDocument.Create(
            Guid.NewGuid(), companyId, employeeId, doc.Id, Guid.NewGuid(), Now);
        empDoc.Archive(Guid.NewGuid(), "old", archivedAt);
        db.EmployeeDocuments.Add(empDoc);

        await db.SaveChangesAsync();
        return (doc, empDoc);
    }

    [Fact]
    public async Task HandleAsync_Purges_Documents_Archived_At_Least_90_Days_Ago()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var archivedAt       = Now.AddDays(-PurgeEligibleArchivedEmployeeDocumentsHandler.MinimumRetentionDays);
        var (doc, empDoc)   = await SeedArchived(db, companyId, employeeId, archivedAt, "key/eligible.pdf");
        var storage          = new FakeDocumentStorageService();
        var handler           = BuildHandler(db, storage);

        var result = await handler.HandleAsync(
            new PurgeEligibleArchivedEmployeeDocumentsRequest { CompanyId = companyId },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.PurgedCount);
        Assert.False(await db.EmployeeDocuments.AnyAsync(ed => ed.Id == empDoc.Id));
        Assert.False(await db.Documents.AnyAsync(d => d.Id == doc.Id));
        Assert.Contains("key/eligible.pdf", storage.Deletions);
    }

    [Fact]
    public async Task HandleAsync_Leaves_Documents_Archived_Fewer_Than_90_Days_Untouched()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        // 89 days ago — one day short of the 90-day retention boundary.
        var archivedAt       = Now.AddDays(-(PurgeEligibleArchivedEmployeeDocumentsHandler.MinimumRetentionDays - 1));
        var (doc, empDoc)   = await SeedArchived(db, companyId, employeeId, archivedAt, "key/too-recent.pdf");
        var storage           = new FakeDocumentStorageService();
        var handler            = BuildHandler(db, storage);

        var result = await handler.HandleAsync(
            new PurgeEligibleArchivedEmployeeDocumentsRequest { CompanyId = companyId },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.PurgedCount);
        Assert.True(await db.EmployeeDocuments.AnyAsync(ed => ed.Id == empDoc.Id));
        Assert.True(await db.Documents.AnyAsync(d => d.Id == doc.Id));
        Assert.Empty(storage.Deletions);
    }

    [Fact]
    public async Task HandleAsync_Leaves_Non_Archived_Documents_Untouched()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();

        var docType = DocumentType.Create(Guid.NewGuid(), companyId, "Contract", null, Now);
        db.DocumentTypes.Add(docType);
        var doc = Document.Create(
            Guid.NewGuid(), companyId, employeeId, "Active Doc", null,
            docType.Id, "active.pdf", 1024, "application/pdf",
            "key/active.pdf", null, Guid.NewGuid(), Now);
        db.Documents.Add(doc);
        var empDoc = EmployeeDocument.Create(Guid.NewGuid(), companyId, employeeId, doc.Id, Guid.NewGuid(), Now);
        db.EmployeeDocuments.Add(empDoc);
        await db.SaveChangesAsync();

        var storage = new FakeDocumentStorageService();
        var handler  = BuildHandler(db, storage);

        var result = await handler.HandleAsync(
            new PurgeEligibleArchivedEmployeeDocumentsRequest { CompanyId = companyId },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.PurgedCount);
        Assert.True(await db.EmployeeDocuments.AnyAsync(ed => ed.Id == empDoc.Id));
        Assert.Empty(storage.Deletions);
    }

    [Fact]
    public async Task HandleAsync_Keeps_Document_And_Skips_Storage_Delete_When_Another_EmployeeDocument_Still_Links_To_It()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var archivedAt       = Now.AddDays(-PurgeEligibleArchivedEmployeeDocumentsHandler.MinimumRetentionDays);

        var docType = DocumentType.Create(Guid.NewGuid(), companyId, "Contract", null, Now);
        db.DocumentTypes.Add(docType);
        var doc = Document.Create(
            Guid.NewGuid(), companyId, employeeId, "Shared Doc", null,
            docType.Id, "shared.pdf", 1024, "application/pdf",
            "key/shared.pdf", null, Guid.NewGuid(), Now);
        db.Documents.Add(doc);

        var eligibleEmpDoc = EmployeeDocument.Create(Guid.NewGuid(), companyId, employeeId, doc.Id, Guid.NewGuid(), Now);
        eligibleEmpDoc.Archive(Guid.NewGuid(), "old", archivedAt);
        db.EmployeeDocuments.Add(eligibleEmpDoc);

        // Second EmployeeDocument still links to the same Document row — the underlying Document
        // must survive the purge even though eligibleEmpDoc is removed.
        var otherEmpDoc = EmployeeDocument.Create(Guid.NewGuid(), companyId, otherEmployeeId, doc.Id, Guid.NewGuid(), Now);
        db.EmployeeDocuments.Add(otherEmpDoc);

        await db.SaveChangesAsync();

        var storage = new FakeDocumentStorageService();
        var handler  = BuildHandler(db, storage);

        var result = await handler.HandleAsync(
            new PurgeEligibleArchivedEmployeeDocumentsRequest { CompanyId = companyId },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.PurgedCount);
        Assert.False(await db.EmployeeDocuments.AnyAsync(ed => ed.Id == eligibleEmpDoc.Id));
        Assert.True(await db.EmployeeDocuments.AnyAsync(ed => ed.Id == otherEmpDoc.Id));
        Assert.True(await db.Documents.AnyAsync(d => d.Id == doc.Id));
        Assert.Empty(storage.Deletions);
    }

    [Fact]
    public async Task HandleAsync_Publishes_EmployeeDocumentPurged_Audit_Event_Per_Purged_Row()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var archivedAt       = Now.AddDays(-PurgeEligibleArchivedEmployeeDocumentsHandler.MinimumRetentionDays);
        var (_, empDoc1)    = await SeedArchived(db, companyId, employeeId, archivedAt, "key/one.pdf");
        var (_, empDoc2)    = await SeedArchived(db, companyId, employeeId, archivedAt, "key/two.pdf");
        var audit             = new FakeAuditPublisher();
        var purgedBy          = Guid.NewGuid();
        var handler            = BuildHandler(db, auditPublisher: audit);

        var result = await handler.HandleAsync(
            new PurgeEligibleArchivedEmployeeDocumentsRequest { CompanyId = companyId },
            purgedBy,
            CancellationToken.None);

        Assert.Equal(2, result.Value!.PurgedCount);
        Assert.Equal(2, audit.Published.Count);
        Assert.All(audit.Published, evt =>
        {
            Assert.Equal("employee_document.purged", evt.EventType);
            Assert.Equal("EmployeeDocument",         evt.EntityType);
            Assert.Equal(purgedBy,                   evt.ActorUserId);
        });
        Assert.Contains(audit.Published, evt => evt.EntityId == empDoc1.Id);
        Assert.Contains(audit.Published, evt => evt.EntityId == empDoc2.Id);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Purge_Documents_For_Other_Companies()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var otherCompanyId  = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var archivedAt       = Now.AddDays(-PurgeEligibleArchivedEmployeeDocumentsHandler.MinimumRetentionDays);
        await SeedArchived(db, otherCompanyId, employeeId, archivedAt, "key/other-company.pdf");
        var storage           = new FakeDocumentStorageService();
        var handler             = BuildHandler(db, storage);

        var result = await handler.HandleAsync(
            new PurgeEligibleArchivedEmployeeDocumentsRequest { CompanyId = companyId },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.PurgedCount);
        Assert.Empty(storage.Deletions);
    }
}
