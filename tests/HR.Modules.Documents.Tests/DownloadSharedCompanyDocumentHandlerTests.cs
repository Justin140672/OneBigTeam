using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.DownloadSharedCompanyDocument;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class DownloadSharedCompanyDocumentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Manager_Can_Download_A_Draft_Document()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, false, null, null, Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new DownloadSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            Guid.NewGuid(), callerCanManage: true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("key/p.pdf", result.Value!.ToString());
    }

    [Fact]
    public async Task HandleAsync_NonManager_Cannot_Download_A_Draft_Document()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, false, null, null, Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new DownloadSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            Guid.NewGuid(), callerCanManage: false, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_NonManager_Can_Download_A_Published_Document_In_Their_Audience()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, false, null, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        db.SharedCompanyDocumentAudienceRules.Add(SharedCompanyDocumentAudienceRule.Create(
            Guid.NewGuid(), companyId, doc.Id, SharedCompanyDocumentAudienceRuleType.Department, departmentId));
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader();
        audienceReader.EmployeeAudiences[caller] = new EmployeeAudienceProfile(departmentId, null, null);

        var result = await Handler(db, audienceReader: audienceReader).HandleAsync(
            new DownloadSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            caller, callerCanManage: false, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_NonManager_Cannot_Download_A_Published_Document_Outside_Their_Audience()
    {
        await using var db = BuildContext();
        var companyId    = Guid.NewGuid();
        var category     = await SeedCategory(db, companyId);
        var caller        = Guid.NewGuid();
        var departmentId  = Guid.NewGuid();
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, false, null, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        db.SharedCompanyDocumentAudienceRules.Add(SharedCompanyDocumentAudienceRule.Create(
            Guid.NewGuid(), companyId, doc.Id, SharedCompanyDocumentAudienceRuleType.Department, departmentId));
        await db.SaveChangesAsync();

        // Caller has no seeded audience entry, so their department is null — doesn't match.
        var result = await Handler(db).HandleAsync(
            new DownloadSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            caller, callerCanManage: false, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Unknown_Document()
    {
        await using var db = BuildContext();

        var result = await Handler(db).HandleAsync(
            new DownloadSharedCompanyDocumentRequest { CompanyId = Guid.NewGuid(), DocumentId = Guid.NewGuid() },
            Guid.NewGuid(), callerCanManage: true, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Publishes_Audit_Event_On_Successful_Download()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var category   = await SeedCategory(db, companyId);
        var downloader = Guid.NewGuid();
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Remote Working Policy", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, false, null, null, Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var audit = new FakeAuditPublisher();
        await Handler(db, auditPublisher: audit).HandleAsync(
            new DownloadSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            downloader, callerCanManage: true, CancellationToken.None);

        var evt = Assert.Single(audit.Published);
        Assert.Equal("shared_company_document.downloaded", evt.EventType);
        Assert.Equal("SharedCompanyDocument",               evt.EntityType);
        Assert.Equal(doc.Id,                                evt.EntityId);
        Assert.Equal(companyId,                              evt.CompanyId);
        Assert.Equal(downloader,                             evt.ActorUserId);
        Assert.Equal(downloader,                             evt.ActorEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_Event_When_Access_Is_Denied()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, false, null, null, Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var audit = new FakeAuditPublisher();
        await Handler(db, auditPublisher: audit).HandleAsync(
            new DownloadSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            Guid.NewGuid(), callerCanManage: false, CancellationToken.None); // draft, not a manager

        Assert.Empty(audit.Published);
    }

    private static readonly DateTime FixedUtcNow = new(2026, 7, 13, 10, 0, 0, DateTimeKind.Utc);

    private static DownloadSharedCompanyDocumentHandler Handler(
        DocumentsDbContext db,
        FakeDocumentStorageService? storage = null,
        FakeEmployeeAudienceReader? audienceReader = null,
        FakeAuditPublisher? auditPublisher = null) =>
        new(db,
            storage ?? new FakeDocumentStorageService(),
            new SharedCompanyDocumentAudienceMatcher(db, audienceReader ?? new FakeEmployeeAudienceReader()),
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
