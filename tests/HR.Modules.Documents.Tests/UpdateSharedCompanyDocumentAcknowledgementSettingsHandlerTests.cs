using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.UpdateSharedCompanyDocumentAcknowledgementSettings;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class UpdateSharedCompanyDocumentAcknowledgementSettingsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTime FixedUtcNow = new(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Sets_Acknowledgement_Settings()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var updatedBy = Guid.NewGuid();
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new UpdateSharedCompanyDocumentAcknowledgementSettingsRequest
            {
                CompanyId               = companyId,
                DocumentId              = doc.Id,
                RequiresAcknowledgement = true,
                AcknowledgementDueDate  = new DateOnly(2027, 1, 1),
                AcknowledgementStatement = "I confirm I have read the updated expenses policy.",
            },
            updatedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.RequiresAcknowledgement);
        Assert.Equal(new DateOnly(2027, 1, 1), result.Value.AcknowledgementDueDate);
        Assert.Equal("I confirm I have read the updated expenses policy.", result.Value.AcknowledgementStatement);

        var stored = await db.SharedCompanyDocuments.AsNoTracking().SingleAsync(d => d.Id == doc.Id);
        Assert.True(stored.RequiresAcknowledgement);
        Assert.Equal(new DateOnly(2027, 1, 1), stored.AcknowledgementDueDate);
        Assert.Equal(updatedBy, stored.UpdatedBy);
    }

    [Fact]
    public async Task HandleAsync_Clears_DueDate_And_Statement_When_Turning_Acknowledgement_Off()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, requiresAcknowledgement: true, acknowledgementDueDate: new DateOnly(2027, 1, 1),
            acknowledgementStatement: "Old statement", createdBy: Guid.NewGuid(), now: Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new UpdateSharedCompanyDocumentAcknowledgementSettingsRequest
            {
                CompanyId               = companyId,
                DocumentId              = doc.Id,
                RequiresAcknowledgement = false,
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.RequiresAcknowledgement);
        Assert.Null(result.Value.AcknowledgementDueDate);
        Assert.Null(result.Value.AcknowledgementStatement);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Unknown_Document()
    {
        await using var db = BuildContext();

        var result = await Handler(db).HandleAsync(
            new UpdateSharedCompanyDocumentAcknowledgementSettingsRequest { CompanyId = Guid.NewGuid(), DocumentId = Guid.NewGuid() },
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
            new UpdateSharedCompanyDocumentAcknowledgementSettingsRequest { CompanyId = Guid.NewGuid(), DocumentId = doc.Id },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Publishes_Audit_Event_When_Settings_Change()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var updatedBy = Guid.NewGuid();
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var audit = new FakeAuditPublisher();
        await Handler(db, audit).HandleAsync(
            new UpdateSharedCompanyDocumentAcknowledgementSettingsRequest
            {
                CompanyId               = companyId,
                DocumentId              = doc.Id,
                RequiresAcknowledgement = true,
                AcknowledgementDueDate  = new DateOnly(2027, 1, 1),
            },
            updatedBy, CancellationToken.None);

        var evt = Assert.Single(audit.Published);
        Assert.Equal("shared_company_document.acknowledgement_settings_updated", evt.EventType);
        Assert.Equal("SharedCompanyDocument",                                    evt.EntityType);
        Assert.Equal(doc.Id,                                                     evt.EntityId);
        Assert.Equal(updatedBy,                                                  evt.ActorUserId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_Event_When_Nothing_Changed()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid());
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var audit = new FakeAuditPublisher();
        await Handler(db, audit).HandleAsync(
            new UpdateSharedCompanyDocumentAcknowledgementSettingsRequest
            {
                CompanyId               = companyId,
                DocumentId              = doc.Id,
                RequiresAcknowledgement = false, // same as CreateDoc's default
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(audit.Published);
    }

    private static UpdateSharedCompanyDocumentAcknowledgementSettingsHandler Handler(
        DocumentsDbContext db, FakeAuditPublisher? auditPublisher = null) =>
        new(db, auditPublisher ?? new FakeAuditPublisher(), new FakeClock(FixedUtcNow));

    private static SharedCompanyDocument CreateDoc(Guid companyId, Guid categoryId, Guid createdBy) =>
        SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, categoryId, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, false, null, null, createdBy, Now);

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
