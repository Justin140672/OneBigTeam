using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.UploadRequestedDocument;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Modules.Documents.Tests;

public class UploadRequestedDocumentHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 29, 10, 0, 0, DateTimeKind.Utc);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static (UploadRequestedDocumentHandler Handler, FakeTaskCompleter TaskCompleter, FakeAuditPublisher Audit) BuildHandler(
        DocumentsDbContext db,
        FakeDocumentStorageService? storage = null,
        FakeVirusScanService? scanner = null,
        FileUploadOptions? options = null)
    {
        var taskCompleter = new FakeTaskCompleter();
        var audit         = new FakeAuditPublisher();
        var handler = new UploadRequestedDocumentHandler(
            db,
            storage ?? new FakeDocumentStorageService(),
            new FileUploadValidator(Options.Create(options ?? new FileUploadOptions())),
            scanner ?? new FakeVirusScanService(),
            taskCompleter,
            new FakeClock(FixedUtcNow),
            audit);
        return (handler, taskCompleter, audit);
    }

    private static async Task<DocumentType> SeedDocumentType(DocumentsDbContext db, Guid companyId, Guid? id = null)
    {
        var dt = DocumentType.Create(id ?? Guid.NewGuid(), companyId, "Passport", null, DateTimeOffset.UtcNow);
        db.DocumentTypes.Add(dt);
        await db.SaveChangesAsync();
        return dt;
    }

    private static async Task<DocumentRequest> SeedRequest(
        DocumentsDbContext db,
        Guid companyId,
        Guid employeeId,
        Guid documentTypeId,
        DocumentRequestStatus status = DocumentRequestStatus.Requested)
    {
        var r = DocumentRequest.Create(
            Guid.NewGuid(), companyId, employeeId, documentTypeId,
            positionProfileRequiredDocumentId: null, dueDate: null,
            requestedByEmployeeId: null, DateTimeOffset.UtcNow);
        if (status == DocumentRequestStatus.Uploaded)
            r.MarkUploaded(employeeId, DateTimeOffset.UtcNow);
        db.DocumentRequests.Add(r);
        await db.SaveChangesAsync();
        return r;
    }

    private static IFormFile FakePdfFile() =>
        new FormFile(new MemoryStream(PdfBytes()), 0, PdfBytes().Length, "File", "passport.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf",
        };

    private static byte[] PdfBytes()
    {
        var magic = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        var bytes = new byte[magic.Length + 1020];
        magic.CopyTo(bytes, 0);
        return bytes;
    }

    private static UploadRequestedDocumentRequest BuildRequest(
        Guid companyId, Guid employeeId, Guid documentRequestId,
        IFormFile? file = null, string title = "My Passport") =>
        new()
        {
            CompanyId         = companyId,
            EmployeeId        = employeeId,
            DocumentRequestId = documentRequestId,
            Title             = title,
            File              = file ?? FakePdfFile(),
        };

    // ── Success path ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Creates_Document_And_EmployeeDocument()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var dt         = await SeedDocumentType(db, companyId);
        var req        = await SeedRequest(db, companyId, employeeId, dt.Id);
        var (handler, _, _) = BuildHandler(db);

        var result = await handler.HandleAsync(BuildRequest(companyId, employeeId, req.Id), employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(await db.Documents.ToListAsync());
        Assert.Single(await db.EmployeeDocuments.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Marks_DocumentRequest_Uploaded()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var dt         = await SeedDocumentType(db, companyId);
        var req        = await SeedRequest(db, companyId, employeeId, dt.Id);
        var (handler, _, _) = BuildHandler(db);

        await handler.HandleAsync(BuildRequest(companyId, employeeId, req.Id), employeeId, CancellationToken.None);

        var saved = await db.DocumentRequests.SingleAsync();
        Assert.Equal(DocumentRequestStatus.Uploaded, saved.Status);
        Assert.NotNull(saved.CompletedAt);
        Assert.Equal(employeeId, saved.CompletedByEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Completes_Upload_Task()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var dt         = await SeedDocumentType(db, companyId);
        var req        = await SeedRequest(db, companyId, employeeId, dt.Id);
        var (handler, taskCompleter, _) = BuildHandler(db);

        await handler.HandleAsync(BuildRequest(companyId, employeeId, req.Id), employeeId, CancellationToken.None);

        Assert.Equal(1, taskCompleter.CallCount);
        var call = taskCompleter.Calls[0];
        Assert.Equal(req.Id,              call.SourceEntityId);
        Assert.Equal(TaskSource.Document, call.Source);
        Assert.Equal(TaskActionType.Upload, call.ActionType);
        Assert.Equal(employeeId,          call.CompletedBy);
    }

    [Fact]
    public async Task HandleAsync_Publishes_Two_Audit_Events()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var dt         = await SeedDocumentType(db, companyId);
        var req        = await SeedRequest(db, companyId, employeeId, dt.Id);
        var (handler, _, audit) = BuildHandler(db);

        await handler.HandleAsync(BuildRequest(companyId, employeeId, req.Id), employeeId, CancellationToken.None);

        Assert.Equal(2, audit.Published.Count);
        Assert.Contains(audit.Published, e => e.EventType == "document.uploaded");
        Assert.Contains(audit.Published, e => e.EventType == "document_request.fulfilled");
    }

    [Fact]
    public async Task HandleAsync_Response_Includes_DocumentRequestId()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var dt         = await SeedDocumentType(db, companyId);
        var req        = await SeedRequest(db, companyId, employeeId, dt.Id);
        var (handler, _, _) = BuildHandler(db);

        var result = await handler.HandleAsync(BuildRequest(companyId, employeeId, req.Id), employeeId, CancellationToken.None);

        Assert.Equal(req.Id, result.Value!.DocumentRequestId);
    }

    // ── Validation failures ───────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Request_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var (handler, _, _) = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Request_Belongs_To_Different_Employee()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var ownerEmployeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var dt  = await SeedDocumentType(db, companyId);
        var req = await SeedRequest(db, companyId, ownerEmployeeId, dt.Id);
        var (handler, _, _) = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, otherEmployeeId, req.Id),
            otherEmployeeId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Request_Already_Uploaded()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var dt  = await SeedDocumentType(db, companyId);
        var req = await SeedRequest(db, companyId, employeeId, dt.Id, DocumentRequestStatus.Uploaded);
        var (handler, _, _) = BuildHandler(db);

        var result = await handler.HandleAsync(BuildRequest(companyId, employeeId, req.Id), employeeId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_DocumentType_Inactive()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var dt  = await SeedDocumentType(db, companyId);
        dt.Deactivate(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        var req = await SeedRequest(db, companyId, employeeId, dt.Id);
        var (handler, _, _) = BuildHandler(db);

        var result = await handler.HandleAsync(BuildRequest(companyId, employeeId, req.Id), employeeId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_File_Too_Large()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var dt  = await SeedDocumentType(db, companyId);
        var req = await SeedRequest(db, companyId, employeeId, dt.Id);
        var (handler, _, _) = BuildHandler(db, options: new FileUploadOptions { MaxFileSizeBytes = 10 });

        var result = await handler.HandleAsync(BuildRequest(companyId, employeeId, req.Id), employeeId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_File_Infected()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var dt  = await SeedDocumentType(db, companyId);
        var req = await SeedRequest(db, companyId, employeeId, dt.Id);
        var (handler, _, _) = BuildHandler(db, scanner: new FakeVirusScanService { ReturnInfected = true, ThreatName = "EICAR" });

        var result = await handler.HandleAsync(BuildRequest(companyId, employeeId, req.Id), employeeId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("EICAR", result.Error.Message);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_When_Validation_Fails()
    {
        await using var db = BuildContext();
        var (handler, _, audit) = BuildHandler(db);

        await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(audit.Published);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Complete_Task_When_Validation_Fails()
    {
        await using var db = BuildContext();
        var (handler, taskCompleter, _) = BuildHandler(db);

        await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(0, taskCompleter.CallCount);
    }
}
