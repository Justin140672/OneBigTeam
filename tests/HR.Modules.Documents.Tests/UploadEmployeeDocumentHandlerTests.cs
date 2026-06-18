using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.UploadEmployeeDocument;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Modules.Documents.Tests;

public class UploadEmployeeDocumentHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 18, 10, 0, 0, DateTimeKind.Utc);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static UploadEmployeeDocumentHandler BuildHandler(
        DocumentsDbContext db,
        FakeDocumentStorageService? storage = null,
        FakeVirusScanService? scanner = null,
        FileUploadOptions? options = null) =>
        new(db,
            storage ?? new FakeDocumentStorageService(),
            new FileUploadValidator(Options.Create(options ?? new FileUploadOptions())),
            scanner ?? new FakeVirusScanService(),
            new FakeClock(FixedUtcNow));

    private static async Task<DocumentType> SeedDocumentType(
        DocumentsDbContext db,
        Guid companyId,
        Guid? id = null)
    {
        var docType = DocumentType.Create(id ?? Guid.NewGuid(), companyId, "Contract", null, DateTimeOffset.UtcNow);
        db.DocumentTypes.Add(docType);
        await db.SaveChangesAsync();
        return docType;
    }

    private static IFormFile FakeFile(
        string fileName    = "contract.pdf",
        string contentType = "application/pdf",
        int size           = 1024) =>
        new FormFile(new MemoryStream(new byte[size]), 0, size, "File", fileName)
        {
            Headers     = new HeaderDictionary(),
            ContentType = contentType,
        };

    private static UploadEmployeeDocumentRequest BuildRequest(
        Guid companyId,
        Guid employeeId,
        Guid documentTypeId,
        IFormFile? file      = null,
        string title         = "Employment Contract",
        string? description  = null,
        DateOnly? expiryDate = null) =>
        new()
        {
            CompanyId      = companyId,
            EmployeeId     = employeeId,
            DocumentTypeId = documentTypeId,
            Title          = title,
            Description    = description,
            ExpiryDate     = expiryDate,
            File           = file ?? FakeFile(),
        };

    [Fact]
    public async Task HandleAsync_CreatesDocument_And_EmployeeDocument()
    {
        await using var db = BuildContext();
        var storage        = new FakeDocumentStorageService();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var uploadedBy     = Guid.NewGuid();
        var docType        = await SeedDocumentType(db, companyId);
        var handler        = BuildHandler(db, storage);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employeeId, docType.Id,
                expiryDate: new DateOnly(2027, 1, 1)),
            uploadedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(companyId,               result.Value!.CompanyId);
        Assert.Equal(employeeId,              result.Value.EmployeeId);
        Assert.Equal(docType.Id,              result.Value.DocumentTypeId);
        Assert.Equal("Employment Contract",   result.Value.Title);
        Assert.Equal("contract.pdf",          result.Value.FileName);
        Assert.Equal(new DateOnly(2027, 1, 1), result.Value.ExpiryDate);

        var savedDoc = await db.Documents.SingleAsync();
        Assert.Equal(result.Value.DocumentId, savedDoc.Id);
        Assert.Equal(employeeId,              savedDoc.EmployeeId);
        Assert.Equal(uploadedBy,              savedDoc.UploadedBy);

        var savedEmployeeDoc = await db.EmployeeDocuments.SingleAsync();
        Assert.Equal(result.Value.EmployeeDocumentId, savedEmployeeDoc.Id);
        Assert.Equal(savedDoc.Id,  savedEmployeeDoc.DocumentId);
        Assert.Equal(employeeId,   savedEmployeeDoc.EmployeeId);
        Assert.Equal(uploadedBy,   savedEmployeeDoc.AddedBy);

        Assert.Single(storage.Uploads);
        Assert.Equal("contract.pdf", storage.Uploads[0].FileName);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_DocumentType_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_DocumentType_Is_Inactive()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var docType        = await SeedDocumentType(db, companyId);
        docType.Deactivate(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, Guid.NewGuid(), docType.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_DocumentType_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var companyA       = Guid.NewGuid();
        var docType        = await SeedDocumentType(db, companyA);
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid(), docType.Id),
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
        var docType        = await SeedDocumentType(db, companyId);
        var handler        = BuildHandler(db, options: new FileUploadOptions { MaxFileSizeBytes = 100 });

        var result = await handler.HandleAsync(
            BuildRequest(companyId, Guid.NewGuid(), docType.Id, file: FakeFile(size: 200)),
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
        var docType        = await SeedDocumentType(db, companyId);
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, Guid.NewGuid(), docType.Id,
                file: FakeFile("malware.exe", "application/octet-stream")),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains(".exe", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_ContentType_Not_Allowed()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var docType        = await SeedDocumentType(db, companyId);
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, Guid.NewGuid(), docType.Id,
                file: FakeFile("page.pdf", "text/html")),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_File_Is_Infected()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var docType        = await SeedDocumentType(db, companyId);
        var scanner        = new FakeVirusScanService { ReturnInfected = true, ThreatName = "EICAR.Test.File" };
        var handler        = BuildHandler(db, scanner: scanner);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, Guid.NewGuid(), docType.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("EICAR.Test.File", result.Error.Message);
    }

    [Fact]
    public async Task HandleAsync_Trims_Title_And_Description()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var docType        = await SeedDocumentType(db, companyId);
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, Guid.NewGuid(), docType.Id,
                title: "  Employment Contract  ", description: "  Some notes  "),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Employment Contract", result.Value!.Title);

        var saved = await db.Documents.SingleAsync();
        Assert.Equal("Employment Contract", saved.Title);
        Assert.Equal("Some notes",          saved.Description);
    }

    [Fact]
    public async Task HandleAsync_StorageKey_Contains_CompanyId_And_EmployeeId()
    {
        await using var db = BuildContext();
        var storage        = new FakeDocumentStorageService();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var docType        = await SeedDocumentType(db, companyId);
        var handler        = BuildHandler(db, storage);

        await handler.HandleAsync(
            BuildRequest(companyId, employeeId, docType.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        var storageKey = storage.Uploads[0].StorageKey;
        Assert.Contains(companyId.ToString(),  storageKey);
        Assert.Contains(employeeId.ToString(), storageKey);
    }

    [Fact]
    public async Task HandleAsync_Handles_Null_Description_And_ExpiryDate()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var docType        = await SeedDocumentType(db, companyId);
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, Guid.NewGuid(), docType.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.ExpiryDate);

        var saved = await db.Documents.SingleAsync();
        Assert.Null(saved.Description);
        Assert.Null(saved.ExpiryDate);
    }
}
