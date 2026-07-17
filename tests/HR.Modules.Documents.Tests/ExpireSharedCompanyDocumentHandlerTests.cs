using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.ExpireSharedCompanyDocument;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class ExpireSharedCompanyDocumentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTime FixedUtcNow = new(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Expires_Published_Document_Successfully()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var category   = await SeedCategory(db, companyId);
        var expiredBy  = Guid.NewGuid();
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ExpireSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            expiredBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Expired", result.Value!.Status);
        Assert.Equal(expiredBy, result.Value.ExpiredBy);
        Assert.Equal(FixedUtcNow, result.Value.ExpiredAt);

        var stored = await db.SharedCompanyDocuments.AsNoTracking().SingleAsync(d => d.Id == doc.Id);
        Assert.Equal(SharedCompanyDocumentStatus.Expired, stored.Status);
        Assert.Equal(expiredBy, stored.ExpiredBy);
        Assert.Equal(FixedUtcNow, stored.ExpiredAt);
    }

    [Fact]
    public async Task HandleAsync_Expires_Document_And_Cancels_Open_Review_Tasks()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var category   = await SeedCategory(db, companyId);
        var expiredBy  = Guid.NewGuid();
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var taskCanceller = new FakeTaskCanceller { CancelAllReturnCount = 2 };

        var result = await Handler(db, taskCanceller: taskCanceller).HandleAsync(
            new ExpireSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            expiredBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Expired", result.Value!.Status);
        Assert.Equal(2, result.Value.ReviewTasksCancelled);

        Assert.Equal(1, taskCanceller.CancelAllCallCount);
        var call = Assert.Single(taskCanceller.CancelAllCalls);
        Assert.Equal(companyId, call.CompanyId);
        Assert.Equal(doc.Id, call.SourceEntityId);
        Assert.Equal(TaskSource.Document, call.Source);
        Assert.Equal(TaskActionType.Review, call.ActionType);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Document_Already_Expired()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        doc.MarkExpired(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var taskCanceller = new FakeTaskCanceller();

        var result = await Handler(db, taskCanceller: taskCanceller).HandleAsync(
            new ExpireSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal("This document is already expired.", result.Error.Message);
        Assert.Equal(0, taskCanceller.CancelAllCallCount);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Document_Already_Archived()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        doc.Archive(Guid.NewGuid(), "No longer required", Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var taskCanceller = new FakeTaskCanceller();

        var result = await Handler(db, taskCanceller: taskCanceller).HandleAsync(
            new ExpireSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal("An archived document cannot be marked expired.", result.Error.Message);
        Assert.Equal(0, taskCanceller.CancelAllCallCount);

        var stored = await db.SharedCompanyDocuments.AsNoTracking().SingleAsync(d => d.Id == doc.Id);
        Assert.Equal(SharedCompanyDocumentStatus.Archived, stored.Status);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Unknown_Document()
    {
        await using var db = BuildContext();

        var result = await Handler(db).HandleAsync(
            new ExpireSharedCompanyDocumentRequest { CompanyId = Guid.NewGuid(), DocumentId = Guid.NewGuid() },
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
            new ExpireSharedCompanyDocumentRequest { CompanyId = Guid.NewGuid(), DocumentId = doc.Id },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Sets_UpdatedBy_And_UpdatedAt_Alongside_ExpiredBy_And_ExpiredAt()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var expiredBy = Guid.NewGuid();
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ExpireSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            expiredBy, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var stored = await db.SharedCompanyDocuments.AsNoTracking().SingleAsync(d => d.Id == doc.Id);
        Assert.Equal(expiredBy, stored.ExpiredBy);
        Assert.Equal(FixedUtcNow, stored.ExpiredAt);
        Assert.Equal(expiredBy, stored.UpdatedBy);
        Assert.Equal(FixedUtcNow, stored.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_Publishes_Audit_Event()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var category   = await SeedCategory(db, companyId);
        var expiredBy  = Guid.NewGuid();
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var audit = new FakeAuditPublisher();
        await Handler(db, auditPublisher: audit).HandleAsync(
            new ExpireSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            expiredBy, CancellationToken.None);

        var evt = Assert.Single(audit.Published);
        Assert.Equal("shared_company_document.expired", evt.EventType);
        Assert.Equal("SharedCompanyDocument",            evt.EntityType);
        Assert.Equal(doc.Id,                              evt.EntityId);
        Assert.Equal(expiredBy,                            evt.ActorUserId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_Event_On_Failure()
    {
        await using var db = BuildContext();

        var audit = new FakeAuditPublisher();
        await Handler(db, auditPublisher: audit).HandleAsync(
            new ExpireSharedCompanyDocumentRequest { CompanyId = Guid.NewGuid(), DocumentId = Guid.NewGuid() },
            Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(audit.Published);
    }

    private static ExpireSharedCompanyDocumentHandler Handler(
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
