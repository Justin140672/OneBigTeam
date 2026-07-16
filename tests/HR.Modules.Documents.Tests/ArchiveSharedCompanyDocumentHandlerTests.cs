using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.ArchiveSharedCompanyDocument;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class ArchiveSharedCompanyDocumentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTime FixedUtcNow = new(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Archives_Draft_Document_Successfully()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var category   = await SeedCategory(db, companyId);
        var archivedBy = Guid.NewGuid();
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ArchiveSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id, Reason = "  No longer required.  " },
            archivedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Archived", result.Value!.Status);
        Assert.Equal(archivedBy, result.Value.ArchivedBy);
        Assert.Equal("No longer required.", result.Value.ArchiveReason);
        Assert.Equal(FixedUtcNow, result.Value.ArchivedAt);

        var stored = await db.SharedCompanyDocuments.AsNoTracking().SingleAsync(d => d.Id == doc.Id);
        Assert.Equal(SharedCompanyDocumentStatus.Archived, stored.Status);
        Assert.Equal(archivedBy, stored.ArchivedBy);
        Assert.Equal("No longer required.", stored.ArchiveReason);
        Assert.Equal(FixedUtcNow, stored.ArchivedAt);
    }

    [Fact]
    public async Task HandleAsync_Archives_Published_Document_And_Cancels_Open_Acknowledgement_Tasks()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var category   = await SeedCategory(db, companyId);
        var archivedBy = Guid.NewGuid();
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var taskCanceller = new FakeTaskCanceller { CancelAllReturnCount = 3 };

        var result = await Handler(db, taskCanceller: taskCanceller).HandleAsync(
            new ArchiveSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id, Reason = "Superseded" },
            archivedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Archived", result.Value!.Status);
        Assert.Equal(3, result.Value.AcknowledgementTasksCancelled);

        Assert.Equal(1, taskCanceller.CancelAllCallCount);
        var call = Assert.Single(taskCanceller.CancelAllCalls);
        Assert.Equal(companyId, call.CompanyId);
        Assert.Equal(doc.Id, call.SourceEntityId);
        Assert.Equal(TaskSource.Document, call.Source);
        Assert.Equal(TaskActionType.Acknowledge, call.ActionType);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Document_Already_Archived()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        doc.Archive(Guid.NewGuid(), "First reason", Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var taskCanceller = new FakeTaskCanceller();

        var result = await Handler(db, taskCanceller: taskCanceller).HandleAsync(
            new ArchiveSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id, Reason = "Second reason" },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal(0, taskCanceller.CancelAllCallCount);

        var stored = await db.SharedCompanyDocuments.AsNoTracking().SingleAsync(d => d.Id == doc.Id);
        Assert.Equal("First reason", stored.ArchiveReason);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Unknown_Document()
    {
        await using var db = BuildContext();

        var result = await Handler(db).HandleAsync(
            new ArchiveSharedCompanyDocumentRequest { CompanyId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Reason = "Reason" },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Document_Belongs_To_Another_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ArchiveSharedCompanyDocumentRequest { CompanyId = Guid.NewGuid(), DocumentId = doc.Id, Reason = "Reason" },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Sets_UpdatedBy_And_UpdatedAt_Alongside_ArchivedBy_And_ArchivedAt()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var category   = await SeedCategory(db, companyId);
        var archivedBy = Guid.NewGuid();
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ArchiveSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id, Reason = "Reason" },
            archivedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var stored = await db.SharedCompanyDocuments.AsNoTracking().SingleAsync(d => d.Id == doc.Id);
        Assert.Equal(archivedBy, stored.ArchivedBy);
        Assert.Equal(FixedUtcNow, stored.ArchivedAt);
        Assert.Equal(archivedBy, stored.UpdatedBy);
        Assert.Equal(FixedUtcNow, stored.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_Retains_Versions_And_Completed_Acknowledgements_When_Archiving()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var employeeId = Guid.NewGuid();
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        db.SharedCompanyDocuments.Add(doc);

        var version = SharedCompanyDocumentVersion.Create(
            Guid.NewGuid(), companyId, doc.Id, 1, "key/p.pdf", "p.pdf", 100, "application/pdf",
            doc.CreatedBy, Now, versionNote: null, requiresAcknowledgement: false, effectiveDate: null);
        db.SharedCompanyDocumentVersions.Add(version);

        var acknowledgement = SharedCompanyDocumentAcknowledgement.Create(
            Guid.NewGuid(), companyId, doc.Id, employeeId, doc.VersionNumber, "Statement", null, Now);
        db.SharedCompanyDocumentAcknowledgements.Add(acknowledgement);

        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ArchiveSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id, Reason = "Reason" },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var storedDoc = await db.SharedCompanyDocuments.AsNoTracking().SingleAsync(d => d.Id == doc.Id);
        Assert.Equal(SharedCompanyDocumentStatus.Archived, storedDoc.Status);

        var storedVersion = await db.SharedCompanyDocumentVersions.AsNoTracking().SingleAsync(v => v.Id == version.Id);
        Assert.Equal(doc.Id, storedVersion.SharedCompanyDocumentId);
        Assert.Equal(1, storedVersion.VersionNumber);

        var storedAcknowledgement = await db.SharedCompanyDocumentAcknowledgements.AsNoTracking()
            .SingleAsync(a => a.Id == acknowledgement.Id);
        Assert.Equal(employeeId, storedAcknowledgement.EmployeeId);
        Assert.Equal(doc.VersionNumber, storedAcknowledgement.VersionNumber);
    }

    [Fact]
    public async Task HandleAsync_Publishes_Audit_Event()
    {
        await using var db = BuildContext();
        var companyId   = Guid.NewGuid();
        var category    = await SeedCategory(db, companyId);
        var archivedBy  = Guid.NewGuid();
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var audit = new FakeAuditPublisher();
        await Handler(db, auditPublisher: audit).HandleAsync(
            new ArchiveSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id, Reason = "Reason" },
            archivedBy, CancellationToken.None);

        var evt = Assert.Single(audit.Published);
        Assert.Equal("shared_company_document.archived", evt.EventType);
        Assert.Equal("SharedCompanyDocument",             evt.EntityType);
        Assert.Equal(doc.Id,                               evt.EntityId);
        Assert.Equal(archivedBy,                           evt.ActorUserId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_Event_On_Failure()
    {
        await using var db = BuildContext();

        var audit = new FakeAuditPublisher();
        await Handler(db, auditPublisher: audit).HandleAsync(
            new ArchiveSharedCompanyDocumentRequest { CompanyId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Reason = "Reason" },
            Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(audit.Published);
    }

    private static ArchiveSharedCompanyDocumentHandler Handler(
        DocumentsDbContext db,
        FakeTaskCanceller? taskCanceller = null,
        FakeAuditPublisher? auditPublisher = null) =>
        new(
            db,
            taskCanceller ?? new FakeTaskCanceller(),
            auditPublisher ?? new FakeAuditPublisher(),
            new FakeClock(FixedUtcNow));

    private static SharedCompanyDocument CreateDoc(Guid companyId, Guid categoryId, Guid createdBy) =>
        SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, categoryId, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, false, null, null, createdBy, Now);

    private static async Task<CompanyDocumentCategory> SeedCategory(
        DocumentsDbContext db, Guid companyId, string name = "Policy")
    {
        var category = CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, name, Now);
        db.CompanyDocumentCategories.Add(category);
        await db.SaveChangesAsync();
        return category;
    }

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
