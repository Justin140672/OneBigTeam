using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.UpdateSharedCompanyDocumentMetadata;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class UpdateSharedCompanyDocumentMetadataHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTime FixedUtcNow = new(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Updates_Editable_Fields()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var category   = await SeedCategory(db, companyId, "Policy");
        var newCategory = await SeedCategory(db, companyId, "Handbook");
        var createdBy  = Guid.NewGuid();
        var updatedBy  = Guid.NewGuid();

        var doc = CreateDoc(companyId, "Old Title", category.Id, createdBy);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new UpdateSharedCompanyDocumentMetadataRequest
            {
                CompanyId     = companyId,
                DocumentId    = doc.Id,
                Title         = "New Title",
                Description   = "New description",
                CategoryId    = newCategory.Id,
                EffectiveDate = new DateOnly(2026, 9, 1),
                ReviewDate    = new DateOnly(2027, 9, 1),
            },
            updatedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Title",              result.Value!.Title);
        Assert.Equal("New description",        result.Value.Description);
        Assert.Equal(newCategory.Id,           result.Value.CategoryId);
        Assert.Equal(new DateOnly(2026, 9, 1), result.Value.EffectiveDate);
        Assert.Equal(new DateOnly(2027, 9, 1), result.Value.ReviewDate);
        Assert.Equal(updatedBy,                result.Value.UpdatedBy);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Change_CompanyId_CreatedBy_CreatedAt_Or_VersionNumber()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var createdBy = Guid.NewGuid();

        var doc = CreateDoc(companyId, "Title", category.Id, createdBy);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        await Handler(db).HandleAsync(
            new UpdateSharedCompanyDocumentMetadataRequest
            {
                CompanyId  = companyId,
                DocumentId = doc.Id,
                Title      = "Updated Title",
                CategoryId = category.Id,
            },
            Guid.NewGuid(), CancellationToken.None);

        var stored = await db.SharedCompanyDocuments.AsNoTracking().SingleAsync(d => d.Id == doc.Id);
        Assert.Equal(companyId, stored.CompanyId);
        Assert.Equal(createdBy, stored.CreatedBy);
        Assert.Equal(Now,       stored.CreatedAt);
        Assert.Equal(1,         stored.VersionNumber);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Change_Audience_Or_Acknowledgement()
    {
        // Audience/RequiresAcknowledgement aren't in the "Editable fields" list — audience now
        // has its own dedicated endpoint, so metadata updates must leave its rule rows untouched.
        await using var db = BuildContext();
        var companyId    = Guid.NewGuid();
        var category     = await SeedCategory(db, companyId);
        var departmentId = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Title", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, true, null, null, Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        db.SharedCompanyDocumentAudienceRules.Add(SharedCompanyDocumentAudienceRule.Create(
            Guid.NewGuid(), companyId, doc.Id, SharedCompanyDocumentAudienceRuleType.Department, departmentId));
        await db.SaveChangesAsync();

        await Handler(db).HandleAsync(
            new UpdateSharedCompanyDocumentMetadataRequest
            {
                CompanyId  = companyId,
                DocumentId = doc.Id,
                Title      = "New Title",
                CategoryId = category.Id,
            },
            Guid.NewGuid(), CancellationToken.None);

        var stored = await db.SharedCompanyDocuments.AsNoTracking().SingleAsync(d => d.Id == doc.Id);
        var rule = await db.SharedCompanyDocumentAudienceRules.AsNoTracking().SingleAsync(r => r.SharedCompanyDocumentId == doc.Id);
        Assert.Equal(SharedCompanyDocumentAudienceRuleType.Department, rule.RuleType);
        Assert.Equal(departmentId, rule.TargetId);
        Assert.True(stored.RequiresAcknowledgement);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Unknown_Document()
    {
        await using var db = BuildContext();
        var category = await SeedCategory(db, Guid.NewGuid());

        var result = await Handler(db).HandleAsync(
            new UpdateSharedCompanyDocumentMetadataRequest
            {
                CompanyId  = Guid.NewGuid(),
                DocumentId = Guid.NewGuid(),
                Title      = "Title",
                CategoryId = category.Id,
            },
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
        var doc = CreateDoc(companyId, "Title", category.Id, Guid.NewGuid());
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new UpdateSharedCompanyDocumentMetadataRequest
            {
                CompanyId  = Guid.NewGuid(), // wrong company
                DocumentId = doc.Id,
                Title      = "Title",
                CategoryId = category.Id,
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Category_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = CreateDoc(companyId, "Title", category.Id, Guid.NewGuid());
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new UpdateSharedCompanyDocumentMetadataRequest
            {
                CompanyId  = companyId,
                DocumentId = doc.Id,
                Title      = "Title",
                CategoryId = Guid.NewGuid(),
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Category_Belongs_To_Another_Company()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var otherCompanyId  = Guid.NewGuid();
        var category        = await SeedCategory(db, companyId);
        var otherCategory   = await SeedCategory(db, otherCompanyId);
        var doc = CreateDoc(companyId, "Title", category.Id, Guid.NewGuid());
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new UpdateSharedCompanyDocumentMetadataRequest
            {
                CompanyId  = companyId,
                DocumentId = doc.Id,
                Title      = "Title",
                CategoryId = otherCategory.Id,
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Publishes_Audit_Event_When_Something_Changed()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var updatedBy = Guid.NewGuid();
        var doc = CreateDoc(companyId, "Old Title", category.Id, Guid.NewGuid());
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var audit = new FakeAuditPublisher();
        await Handler(db, audit).HandleAsync(
            new UpdateSharedCompanyDocumentMetadataRequest
            {
                CompanyId  = companyId,
                DocumentId = doc.Id,
                Title      = "New Title",
                CategoryId = category.Id,
            },
            updatedBy, CancellationToken.None);

        var evt = Assert.Single(audit.Published);
        Assert.Equal("shared_company_document.metadata_updated", evt.EventType);
        Assert.Equal("SharedCompanyDocument",                    evt.EntityType);
        Assert.Equal(doc.Id,                                     evt.EntityId);
        Assert.Equal(updatedBy,                                  evt.ActorUserId);
        Assert.NotNull(evt.Before);
        Assert.NotNull(evt.After);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_Event_When_Nothing_Changed()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = CreateDoc(companyId, "Same Title", category.Id, Guid.NewGuid());
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var audit = new FakeAuditPublisher();
        await Handler(db, audit).HandleAsync(
            new UpdateSharedCompanyDocumentMetadataRequest
            {
                CompanyId     = companyId,
                DocumentId    = doc.Id,
                Title         = "Same Title",
                Description   = doc.Description,
                CategoryId    = category.Id,
                EffectiveDate = doc.EffectiveDate,
                ReviewDate    = doc.ReviewDate,
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(audit.Published);
    }

    private static UpdateSharedCompanyDocumentMetadataHandler Handler(
        DocumentsDbContext db, FakeAuditPublisher? auditPublisher = null) =>
        new(db, auditPublisher ?? new FakeAuditPublisher(), new FakeClock(FixedUtcNow));

    private static SharedCompanyDocument CreateDoc(Guid companyId, string title, Guid categoryId, Guid createdBy) =>
        SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, title, null, categoryId, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, false, null, null, createdBy, Now);

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
