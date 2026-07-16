using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.DownloadSharedCompanyDocumentVersion;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class DownloadSharedCompanyDocumentVersionHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Downloads_An_Older_Version_Not_The_Current_File()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var owner     = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/v1.pdf", "v1.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, false, null, null, owner, Now);
        doc.ReplaceFile("key/v2.pdf", "v2.pdf", 200, "application/pdf", owner, Now.AddDays(1));
        db.SharedCompanyDocuments.Add(doc);

        db.SharedCompanyDocumentVersions.AddRange(
            SharedCompanyDocumentVersion.Create(Guid.NewGuid(), companyId, doc.Id, 1, "key/v1.pdf", "v1.pdf", 100, "application/pdf", owner, Now, versionNote: null, requiresAcknowledgement: false, effectiveDate: null),
            SharedCompanyDocumentVersion.Create(Guid.NewGuid(), companyId, doc.Id, 2, "key/v2.pdf", "v2.pdf", 200, "application/pdf", owner, Now.AddDays(1), versionNote: "Updated", requiresAcknowledgement: false, effectiveDate: null));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new DownloadSharedCompanyDocumentVersionRequest { CompanyId = companyId, DocumentId = doc.Id, VersionNumber = 1 },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("key/v1.pdf", result.Value!.ToString());
        Assert.DoesNotContain("key/v2.pdf", result.Value.ToString());
    }

    [Fact]
    public async Task HandleAsync_Publishes_Audit_Event_With_Requested_VersionNumber()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var owner      = Guid.NewGuid();
        var downloader = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Remote Working Policy", null, category.Id, "key/v1.pdf", "v1.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, false, null, null, owner, Now);
        doc.ReplaceFile("key/v2.pdf", "v2.pdf", 200, "application/pdf", owner, Now.AddDays(1));
        db.SharedCompanyDocuments.Add(doc);

        db.SharedCompanyDocumentVersions.AddRange(
            SharedCompanyDocumentVersion.Create(Guid.NewGuid(), companyId, doc.Id, 1, "key/v1.pdf", "v1.pdf", 100, "application/pdf", owner, Now, versionNote: null, requiresAcknowledgement: false, effectiveDate: null),
            SharedCompanyDocumentVersion.Create(Guid.NewGuid(), companyId, doc.Id, 2, "key/v2.pdf", "v2.pdf", 200, "application/pdf", owner, Now.AddDays(1), versionNote: "Updated", requiresAcknowledgement: false, effectiveDate: null));
        await db.SaveChangesAsync();

        var audit = new FakeAuditPublisher();
        await Handler(db, auditPublisher: audit).HandleAsync(
            new DownloadSharedCompanyDocumentVersionRequest { CompanyId = companyId, DocumentId = doc.Id, VersionNumber = 1 },
            downloader, CancellationToken.None);

        var evt = Assert.Single(audit.Published);
        Assert.Equal("shared_company_document.downloaded", evt.EventType);
        Assert.Equal("SharedCompanyDocument",               evt.EntityType);
        Assert.Equal(doc.Id,                                evt.EntityId);
        Assert.Equal(companyId,                              evt.CompanyId);
        Assert.Equal(downloader,                             evt.ActorUserId);
        Assert.Equal(downloader,                             evt.ActorEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Unknown_Document()
    {
        await using var db = BuildContext();

        var result = await Handler(db).HandleAsync(
            new DownloadSharedCompanyDocumentVersionRequest { CompanyId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), VersionNumber = 1 },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_VersionNumber_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var owner     = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/v1.pdf", "v1.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, false, null, null, owner, Now);
        db.SharedCompanyDocuments.Add(doc);
        db.SharedCompanyDocumentVersions.Add(
            SharedCompanyDocumentVersion.Create(Guid.NewGuid(), companyId, doc.Id, 1, "key/v1.pdf", "v1.pdf", 100, "application/pdf", owner, Now, versionNote: null, requiresAcknowledgement: false, effectiveDate: null));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new DownloadSharedCompanyDocumentVersionRequest { CompanyId = companyId, DocumentId = doc.Id, VersionNumber = 99 },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Document_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var companyA  = Guid.NewGuid();
        var category  = await SeedCategory(db, companyA);
        var owner     = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyA, "Doc", null, category.Id, "key/v1.pdf", "v1.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, false, null, null, owner, Now);
        db.SharedCompanyDocuments.Add(doc);
        db.SharedCompanyDocumentVersions.Add(
            SharedCompanyDocumentVersion.Create(Guid.NewGuid(), companyA, doc.Id, 1, "key/v1.pdf", "v1.pdf", 100, "application/pdf", owner, Now, versionNote: null, requiresAcknowledgement: false, effectiveDate: null));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new DownloadSharedCompanyDocumentVersionRequest { CompanyId = Guid.NewGuid(), DocumentId = doc.Id, VersionNumber = 1 },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static readonly DateTime FixedUtcNow = new(2026, 7, 13, 10, 0, 0, DateTimeKind.Utc);

    private static DownloadSharedCompanyDocumentVersionHandler Handler(
        DocumentsDbContext db,
        FakeDocumentStorageService? storage = null,
        FakeAuditPublisher? auditPublisher = null) =>
        new(db,
            storage ?? new FakeDocumentStorageService(),
            auditPublisher ?? new FakeAuditPublisher(),
            new FakeClock(FixedUtcNow));

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
