using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.UploadSharedCompanyDocument;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;

namespace HR.Modules.Documents.Tests;

public class UploadSharedCompanyDocumentHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 13, 10, 0, 0, DateTimeKind.Utc);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static UploadSharedCompanyDocumentHandler BuildHandler(
        DocumentsDbContext db,
        FakeDocumentStorageService? storage = null,
        FakeVirusScanService? scanner = null,
        FileUploadOptions? options = null) =>
        new(db,
            storage ?? new FakeDocumentStorageService(),
            new FileUploadValidator(Options.Create(options ?? new FileUploadOptions())),
            scanner ?? new FakeVirusScanService(),
            new FakeClock(FixedUtcNow));

    private static async Task<CompanyDocumentCategory> SeedCategory(
        DocumentsDbContext db, Guid companyId, string name = "Policy")
    {
        var category = CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, name, DateTimeOffset.UtcNow);
        db.CompanyDocumentCategories.Add(category);
        await db.SaveChangesAsync();
        return category;
    }

    // Produces a PDF file with valid magic bytes so magic-byte validation passes.
    private static IFormFile FakePdfFile(string fileName = "policy.pdf", int extraSize = 1020) =>
        FakeFile(fileName, "application/pdf", PdfBytes(extraSize));

    private static IFormFile FakeFile(string fileName, string contentType, byte[] content) =>
        new FormFile(new MemoryStream(content), 0, content.Length, "File", fileName)
        {
            Headers     = new HeaderDictionary(),
            ContentType = contentType,
        };

    // %PDF- followed by padding
    private static byte[] PdfBytes(int extraSize = 1020)
    {
        var magic = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }; // %PDF-
        var bytes = new byte[magic.Length + extraSize];
        magic.CopyTo(bytes, 0);
        return bytes;
    }

    private static UploadSharedCompanyDocumentRequest BuildRequest(
        Guid companyId,
        Guid categoryId,
        IFormFile? file        = null,
        string title           = "Remote Working Policy",
        string? description    = null,
        DateOnly? effectiveDate = null,
        DateOnly? reviewDate    = null) =>
        new()
        {
            CompanyId     = companyId,
            CategoryId    = categoryId,
            Title         = title,
            Description   = description,
            EffectiveDate = effectiveDate,
            ReviewDate    = reviewDate,
            File          = file ?? FakePdfFile(),
        };

    [Fact]
    public async Task HandleAsync_Creates_Document_As_Draft()
    {
        await using var db = BuildContext();
        var storage        = new FakeDocumentStorageService();
        var companyId      = Guid.NewGuid();
        var uploadedBy     = Guid.NewGuid();
        var category       = await SeedCategory(db, companyId);
        var handler        = BuildHandler(db, storage);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, category.Id, effectiveDate: new DateOnly(2027, 1, 1), reviewDate: new DateOnly(2028, 1, 1)),
            uploadedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(companyId,                result.Value!.CompanyId);
        Assert.Equal(category.Id,              result.Value.CategoryId);
        Assert.Equal("Remote Working Policy",  result.Value.Title);
        Assert.Equal("policy.pdf",             result.Value.FileName);
        Assert.Equal(1,                        result.Value.VersionNumber);
        Assert.Equal("Draft",                  result.Value.Status);
        Assert.Equal(new DateOnly(2027, 1, 1), result.Value.EffectiveDate);
        Assert.Equal(new DateOnly(2028, 1, 1), result.Value.ReviewDate);
        Assert.Equal(uploadedBy,               result.Value.CreatedBy);

        var saved = await db.SharedCompanyDocuments.SingleAsync();
        Assert.Equal(SharedCompanyDocumentStatus.Draft, saved.Status);
        Assert.Equal(result.Value.Id, saved.Id);

        Assert.Single(storage.Uploads);
        Assert.Equal("policy.pdf", storage.Uploads[0].FileName);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Category_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid()),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Category_Is_Inactive()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var category       = await SeedCategory(db, companyId);
        var stored          = await db.CompanyDocumentCategories.SingleAsync();
        stored.Deactivate(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, category.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Category_Belongs_To_Different_Company_Even_If_Caller_Has_Manage_Rights()
    {
        // This is the "company ownership" tenant-isolation check: a category from company A
        // must never be usable for a document being created under company B.
        await using var db = BuildContext();
        var companyA        = Guid.NewGuid();
        var category         = await SeedCategory(db, companyA);
        var handler          = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), category.Id), // different company in the request
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_File_Too_Large()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var category       = await SeedCategory(db, companyId);
        var handler        = BuildHandler(db, options: new FileUploadOptions { MaxFileSizeBytes = 100 });

        var result = await handler.HandleAsync(
            BuildRequest(companyId, category.Id, file: FakePdfFile(extraSize: 200)),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("size", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Extension_Not_Allowed()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var category       = await SeedCategory(db, companyId);
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, category.Id,
                file: FakeFile("malware.exe", "application/octet-stream", [0x4D, 0x5A, 0x00, 0x00, 0x00])),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains(".exe", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_File_Is_Infected()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var category       = await SeedCategory(db, companyId);
        var scanner        = new FakeVirusScanService { ReturnInfected = true, ThreatName = "EICAR.Test.File" };
        var handler        = BuildHandler(db, scanner: scanner);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, category.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("EICAR.Test.File", result.Error.Message);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Magic_Bytes_Do_Not_Match_ContentType()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var category       = await SeedCategory(db, companyId);
        var handler        = BuildHandler(db);

        var spoofedFile = FakeFile("legit.pdf", "application/pdf", [0x00, 0x00, 0x00, 0x00, 0x00]);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, category.Id, file: spoofedFile),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("content does not match", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\windows\\system32\\config.pdf")]
    public async Task HandleAsync_Sanitizes_Path_Traversal_Attempts_In_FileName(string maliciousName)
    {
        await using var db = BuildContext();
        var storage         = new FakeDocumentStorageService();
        var companyId       = Guid.NewGuid();
        var category        = await SeedCategory(db, companyId);
        var handler         = BuildHandler(db, storage);

        // The declared extension must still be a valid, allowed one (".pdf") for validation
        // to reach the filename-safety check at all.
        var file = FakeFile(maliciousName, "application/pdf", PdfBytes());

        var result = await handler.HandleAsync(
            BuildRequest(companyId, category.Id, file: file),
            Guid.NewGuid(),
            CancellationToken.None);

        // Path.GetFileName() strips the directory components, so this either succeeds with a
        // safe, traversal-free name, or is rejected outright — it must never reach storage
        // with the raw traversal sequence intact.
        if (result.IsSuccess)
        {
            Assert.DoesNotContain("..", storage.Uploads[0].FileName);
            Assert.DoesNotContain("/", storage.Uploads[0].FileName);
            Assert.DoesNotContain("\\", storage.Uploads[0].FileName);
        }
        else
        {
            Assert.Equal("validation", result.Error.Code);
        }
    }

    [Fact]
    public async Task HandleAsync_StorageKey_Contains_CompanyId()
    {
        await using var db = BuildContext();
        var storage         = new FakeDocumentStorageService();
        var companyId       = Guid.NewGuid();
        var category        = await SeedCategory(db, companyId);
        var handler          = BuildHandler(db, storage);

        await handler.HandleAsync(
            BuildRequest(companyId, category.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Contains(companyId.ToString(), storage.Uploads[0].StorageKey);
    }

    [Fact]
    public async Task HandleAsync_Deletes_StorageObject_When_DbSave_Fails()
    {
        var storage    = new FakeDocumentStorageService();
        var companyId  = Guid.NewGuid();
        var uploadedBy = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ThrowingDocumentsDbContext(options);
        var category       = CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, "Policy", DateTimeOffset.UtcNow);
        db.CompanyDocumentCategories.Add(category);
        await db.BaseSaveChangesAsync(); // seed without throwing

        var handler = BuildHandler(db, storage);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            handler.HandleAsync(
                BuildRequest(companyId, category.Id),
                uploadedBy,
                CancellationToken.None));

        Assert.Single(storage.Uploads);
        Assert.Single(storage.Deletions);
        Assert.Equal(storage.Uploads[0].StorageKey, storage.Deletions[0]);
    }

    [Fact]
    public async Task HandleAsync_Trims_Title_And_Description()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var category       = await SeedCategory(db, companyId);
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, category.Id, title: "  Remote Working Policy  ", description: "  Some notes  "),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Remote Working Policy", result.Value!.Title);

        var saved = await db.SharedCompanyDocuments.SingleAsync();
        Assert.Equal("Remote Working Policy", saved.Title);
        Assert.Equal("Some notes",            saved.Description);
    }

    [Fact]
    public async Task HandleAsync_Handles_Null_Description_And_Dates()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var category       = await SeedCategory(db, companyId);
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, category.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Description);
        Assert.Null(result.Value.EffectiveDate);
        Assert.Null(result.Value.ReviewDate);
    }

    // Subclass used only in the orphan-cleanup test to simulate a DB save failure.
    private sealed class ThrowingDocumentsDbContext(DbContextOptions<DocumentsDbContext> options)
        : DocumentsDbContext(options)
    {
        public Task<int> BaseSaveChangesAsync(CancellationToken ct = default) =>
            base.SaveChangesAsync(ct);

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("Simulated database failure.");
    }
}
