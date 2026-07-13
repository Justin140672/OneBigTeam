using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.DownloadSharedCompanyDocument;
using HR.Modules.Documents.Persistence;
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
            null, null, null, null, false, Guid.NewGuid(), Now);
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
            null, null, null, null, false, Guid.NewGuid(), Now);
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
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, null, null, false, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
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
            null, null, departmentId, null, false, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
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

    private static DownloadSharedCompanyDocumentHandler Handler(
        DocumentsDbContext db, FakeDocumentStorageService? storage = null, FakeEmployeeAudienceReader? audienceReader = null) =>
        new(db, storage ?? new FakeDocumentStorageService(), audienceReader ?? new FakeEmployeeAudienceReader());

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
