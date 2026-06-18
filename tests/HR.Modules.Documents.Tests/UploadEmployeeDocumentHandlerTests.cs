using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.UploadEmployeeDocument;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
        FileUploadOptions? options = null,
        FakeAuditPublisher? auditPublisher = null) =>
        new(db,
            storage ?? new FakeDocumentStorageService(),
            new FileUploadValidator(Options.Create(options ?? new FileUploadOptions())),
            scanner ?? new FakeVirusScanService(),
            new FakeClock(FixedUtcNow),
            auditPublisher ?? new FakeAuditPublisher());

    private static async Task<DocumentType> SeedDocumentType(
        DocumentsDbContext db,
        Guid companyId,
        Guid? id = null,
        bool allowEmployeeUpload = false)
    {
        var docType = DocumentType.Create(id ?? Guid.NewGuid(), companyId, "Contract", null, DateTimeOffset.UtcNow, allowEmployeeUpload);
        db.DocumentTypes.Add(docType);
        await db.SaveChangesAsync();
        return docType;
    }

    // Produces a PDF file with valid magic bytes so magic-byte validation passes.
    private static IFormFile FakePdfFile(int extraSize = 1020) =>
        FakeFile("contract.pdf", "application/pdf", PdfBytes(extraSize));

    private static IFormFile FakeFile(
        string fileName,
        string contentType,
        byte[] content) =>
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

    private static UploadEmployeeDocumentRequest BuildRequest(
        Guid companyId,
        Guid employeeId,
        Guid documentTypeId,
        IFormFile? file      = null,
        string title         = "Employment Contract",
        string? description  = null,
        DateOnly? issueDate  = null,
        DateOnly? expiryDate = null) =>
        new()
        {
            CompanyId      = companyId,
            EmployeeId     = employeeId,
            DocumentTypeId = documentTypeId,
            Title          = title,
            Description    = description,
            IssueDate      = issueDate,
            ExpiryDate     = expiryDate,
            File           = file ?? FakePdfFile(),
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
        Assert.Null(result.Value.IssueDate);

        var savedDoc = await db.Documents.SingleAsync();
        Assert.Equal(result.Value.DocumentId, savedDoc.Id);
        Assert.Equal(employeeId,              savedDoc.EmployeeId);
        Assert.Equal(uploadedBy,              savedDoc.UploadedBy);
        Assert.Null(savedDoc.ExpiryDate); // expiry lives on EmployeeDocument, not Document

        var savedEmployeeDoc = await db.EmployeeDocuments.SingleAsync();
        Assert.Equal(result.Value.EmployeeDocumentId, savedEmployeeDoc.Id);
        Assert.Equal(savedDoc.Id,                     savedEmployeeDoc.DocumentId);
        Assert.Equal(employeeId,                      savedEmployeeDoc.EmployeeId);
        Assert.Equal(uploadedBy,                      savedEmployeeDoc.AddedBy);
        Assert.Equal(new DateOnly(2027, 1, 1),        savedEmployeeDoc.ExpiryDate);

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
            BuildRequest(companyId, Guid.NewGuid(), docType.Id, file: FakePdfFile(extraSize: 200)),
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
                file: FakeFile("malware.exe", "application/octet-stream", [0x4D, 0x5A, 0x00, 0x00, 0x00])),
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
                file: FakeFile("page.pdf", "text/html", PdfBytes())),
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
    public async Task HandleAsync_Returns_Validation_When_Magic_Bytes_Do_Not_Match_ContentType()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var docType        = await SeedDocumentType(db, companyId);
        var handler        = BuildHandler(db);

        // File extension and content type say PDF, but bytes are zeros (renamed/spoofed file)
        var spoofedFile = FakeFile("legit.pdf", "application/pdf", [0x00, 0x00, 0x00, 0x00, 0x00]);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, Guid.NewGuid(), docType.Id, file: spoofedFile),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("content does not match", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Employee_Uploads_Disallowed_Type()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var docType        = await SeedDocumentType(db, companyId, allowEmployeeUpload: false);
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, Guid.NewGuid(), docType.Id),
            uploadedBy:      Guid.NewGuid(),
            isManagerUpload: false,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("does not allow employee uploads", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_When_Employee_Uploads_Allowed_Type()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var docType        = await SeedDocumentType(db, companyId, allowEmployeeUpload: true);
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, Guid.NewGuid(), docType.Id),
            uploadedBy:      Guid.NewGuid(),
            isManagerUpload: false,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Manager_Can_Upload_Disallowed_Type()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var docType        = await SeedDocumentType(db, companyId, allowEmployeeUpload: false);
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, Guid.NewGuid(), docType.Id),
            uploadedBy:      Guid.NewGuid(),
            isManagerUpload: true,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_Deletes_StorageObject_When_DbSave_Fails()
    {
        var storage    = new FakeDocumentStorageService();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var uploadedBy = Guid.NewGuid();

        // Use a context that throws on SaveChangesAsync to simulate a DB failure.
        var options = new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db      = new ThrowingDocumentsDbContext(options);
        var docType             = DocumentType.Create(Guid.NewGuid(), companyId, "Contract", null, DateTimeOffset.UtcNow);
        db.DocumentTypes.Add(docType);
        await db.BaseSaveChangesAsync(); // use base to seed without throwing

        var handler = BuildHandler(db, storage);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            handler.HandleAsync(
                BuildRequest(companyId, employeeId, docType.Id),
                uploadedBy,
                CancellationToken.None));

        // The file that was uploaded must have been cleaned up.
        Assert.Single(storage.Uploads);
        Assert.Single(storage.Deletions);
        Assert.Equal(storage.Uploads[0].StorageKey, storage.Deletions[0]);
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
        Assert.Null(result.Value.IssueDate);

        var saved = await db.Documents.SingleAsync();
        Assert.Null(saved.Description);
        Assert.Null(saved.ExpiryDate);
    }

    [Fact]
    public async Task HandleAsync_Stores_IssueDate_And_ExpiryDate_On_EmployeeDocument()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var docType        = await SeedDocumentType(db, companyId);
        var handler        = BuildHandler(db);
        var issueDate      = new DateOnly(2025, 1, 15);
        var expiryDate     = new DateOnly(2027, 1, 15);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, Guid.NewGuid(), docType.Id,
                issueDate: issueDate, expiryDate: expiryDate),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(issueDate,  result.Value!.IssueDate);
        Assert.Equal(expiryDate, result.Value.ExpiryDate);

        var savedEd = await db.EmployeeDocuments.SingleAsync();
        Assert.Equal(issueDate,  savedEd.IssueDate);
        Assert.Equal(expiryDate, savedEd.ExpiryDate);

        // ExpiryDate must NOT be copied to Document
        var savedDoc = await db.Documents.SingleAsync();
        Assert.Null(savedDoc.ExpiryDate);
    }

    [Fact]
    public async Task HandleAsync_Publishes_DocumentUploaded_Audit_Event()
    {
        await using var db   = BuildContext();
        var audit            = new FakeAuditPublisher();
        var companyId        = Guid.NewGuid();
        var employeeId       = Guid.NewGuid();
        var uploadedBy       = Guid.NewGuid();
        var issueDate        = new DateOnly(2025, 3, 1);
        var expiryDate       = new DateOnly(2027, 3, 1);
        var docType          = await SeedDocumentType(db, companyId);
        var handler          = BuildHandler(db, auditPublisher: audit);

        await handler.HandleAsync(
            BuildRequest(companyId, employeeId, docType.Id,
                issueDate: issueDate, expiryDate: expiryDate),
            uploadedBy,
            isManagerUpload: true,
            CancellationToken.None);

        var evt = Assert.Single(audit.Published);
        Assert.Equal("document.uploaded", evt.EventType);
        Assert.Equal("EmployeeDocument",  evt.EntityType);
        Assert.Equal(companyId,           evt.CompanyId);
        Assert.Equal(uploadedBy,          evt.ActorUserId);
        Assert.Equal("Employment Contract", evt.Summary!.Replace("Document '", "").Replace("' uploaded", ""));
    }

    [Fact]
    public async Task HandleAsync_Audit_Event_Sets_ActorEmployeeId_For_Self_Upload()
    {
        await using var db = BuildContext();
        var audit          = new FakeAuditPublisher();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var docType        = await SeedDocumentType(db, companyId, allowEmployeeUpload: true);
        var handler        = BuildHandler(db, auditPublisher: audit);

        await handler.HandleAsync(
            BuildRequest(companyId, employeeId, docType.Id),
            uploadedBy:      employeeId,   // self-upload: actor IS the employee
            isManagerUpload: false,
            CancellationToken.None);

        var evt = Assert.Single(audit.Published);
        Assert.Equal(employeeId, evt.ActorUserId);
        Assert.Equal(employeeId, evt.ActorEmployeeId); // set for self-uploads
    }

    [Fact]
    public async Task HandleAsync_Audit_Event_Clears_ActorEmployeeId_For_Manager_Upload()
    {
        await using var db = BuildContext();
        var audit          = new FakeAuditPublisher();
        var companyId      = Guid.NewGuid();
        var managerId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var docType        = await SeedDocumentType(db, companyId);
        var handler        = BuildHandler(db, auditPublisher: audit);

        await handler.HandleAsync(
            BuildRequest(companyId, employeeId, docType.Id),
            uploadedBy:      managerId,
            isManagerUpload: true,
            CancellationToken.None);

        var evt = Assert.Single(audit.Published);
        Assert.Equal(managerId, evt.ActorUserId);
        Assert.Null(evt.ActorEmployeeId); // manager is a user, not an employee actor
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_When_Upload_Fails_Validation()
    {
        await using var db = BuildContext();
        var audit          = new FakeAuditPublisher();
        var handler        = BuildHandler(db, auditPublisher: audit);

        await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), // unknown doc type
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Empty(audit.Published);
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
