using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.AcknowledgeSharedCompanyDocument;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class AcknowledgeSharedCompanyDocumentHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 13, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Creates_An_Acknowledgement_Row()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, null, null, requiresAcknowledgement: true, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, caller,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.VersionNumber);

        var saved = await db.SharedCompanyDocumentAcknowledgements.SingleAsync();
        Assert.Equal(caller, saved.EmployeeId);
        Assert.Equal(1,      saved.VersionNumber);
    }

    [Fact]
    public async Task HandleAsync_Is_Idempotent_For_The_Same_Version()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, null, null, requiresAcknowledgement: true, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var handler = Handler(db);
        await handler.HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, caller,
            CancellationToken.None);
        var second = await handler.HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, caller,
            CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Single(db.SharedCompanyDocumentAcknowledgements);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Document_Does_Not_Require_Acknowledgement()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, null, null, requiresAcknowledgement: false, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Draft_Document()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, null, null, requiresAcknowledgement: true, Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Caller_Outside_Audience()
    {
        await using var db = BuildContext();
        var companyId    = Guid.NewGuid();
        var category     = await SeedCategory(db, companyId);
        var departmentId = Guid.NewGuid();
        var caller        = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, departmentId, null, requiresAcknowledgement: true, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, caller,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Reacknowledging_After_A_New_Version_Creates_A_Second_Row()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/v1.pdf", "v1.pdf", 100, "application/pdf",
            null, null, null, null, requiresAcknowledgement: true, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var handler = Handler(db);
        await handler.HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, caller,
            CancellationToken.None);

        var stored = await db.SharedCompanyDocuments.SingleAsync();
        stored.ReplaceFile("key/v2.pdf", "v2.pdf", 200, "application/pdf", Guid.NewGuid(), Now.AddDays(1));
        await db.SaveChangesAsync();

        var second = await handler.HandleAsync(
            new AcknowledgeSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, caller,
            CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Equal(2, second.Value!.VersionNumber);
        Assert.Equal(2, await db.SharedCompanyDocumentAcknowledgements.CountAsync());
    }

    private static AcknowledgeSharedCompanyDocumentHandler Handler(
        DocumentsDbContext db, FakeEmployeeAudienceReader? audienceReader = null) =>
        new(db, audienceReader ?? new FakeEmployeeAudienceReader(), new FakeClock(FixedUtcNow));

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
