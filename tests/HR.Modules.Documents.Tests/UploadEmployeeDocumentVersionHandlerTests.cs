using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.UploadEmployeeDocumentVersion;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Modules.Documents.Tests;

public class UploadEmployeeDocumentVersionHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 18, 10, 0, 0, DateTimeKind.Utc);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static UploadEmployeeDocumentVersionHandler BuildHandler(
        DocumentsDbContext db,
        FakeDocumentStorageService? storage = null,
        FakeTaskCompleter? taskCompleter = null,
        FakeAuditPublisher? auditPublisher = null,
        HR.SharedKernel.IIntegrationEventPublisher? integrationEventPublisher = null,
        Hangfire.IBackgroundJobClient? backgroundJobClient = null) =>
        new(db,
            storage ?? new FakeDocumentStorageService(),
            new FileUploadValidator(Options.Create(new FileUploadOptions())),
            taskCompleter ?? new FakeTaskCompleter(),
            new FakeClock(FixedUtcNow),
            auditPublisher ?? new FakeAuditPublisher(),
            integrationEventPublisher ?? new NoOpIntegrationEventPublisher(),
            backgroundJobClient ?? new NoOpBackgroundJobClient());

    // %PDF- followed by padding
    private static byte[] PdfBytes(int extraSize = 1020)
    {
        var magic = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        var bytes = new byte[magic.Length + extraSize];
        magic.CopyTo(bytes, 0);
        return bytes;
    }

    private static IFormFile FakePdfFile(string fileName = "renewal.pdf") =>
        new FormFile(new MemoryStream(PdfBytes()), 0, PdfBytes().Length, "File", fileName)
        {
            Headers     = new HeaderDictionary(),
            ContentType = "application/pdf",
        };

    private static async Task<(DocumentType docType, Document doc, EmployeeDocument empDoc)> Seed(
        DocumentsDbContext db,
        Guid companyId,
        Guid employeeId,
        string title = "Passport",
        string docTypeName = "Passport")
    {
        var docType = DocumentType.Create(Guid.NewGuid(), companyId, docTypeName, null, DateTimeOffset.UtcNow);
        db.DocumentTypes.Add(docType);

        var doc = Document.Create(
            Guid.NewGuid(), companyId, employeeId, title, "Some description",
            docType.Id, "original.pdf", 1024, "application/pdf",
            $"storage/{Guid.NewGuid():N}/original.pdf",
            null, Guid.NewGuid(), DateTimeOffset.UtcNow);
        db.Documents.Add(doc);

        var empDoc = EmployeeDocument.Create(
            Guid.NewGuid(), companyId, employeeId, doc.Id, Guid.NewGuid(), DateTimeOffset.UtcNow);
        db.EmployeeDocuments.Add(empDoc);

        await db.SaveChangesAsync();
        return (docType, doc, empDoc);
    }

    private static UploadEmployeeDocumentVersionRequest BuildRequest(
        Guid companyId,
        Guid employeeId,
        Guid employeeDocumentId,
        IFormFile? file      = null,
        DateOnly? issueDate  = null,
        DateOnly? expiryDate = null) =>
        new()
        {
            CompanyId          = companyId,
            EmployeeId         = employeeId,
            EmployeeDocumentId = employeeDocumentId,
            IssueDate          = issueDate,
            ExpiryDate         = expiryDate,
            File               = file ?? FakePdfFile(),
        };

    [Fact]
    public async Task HandleAsync_Success_Creates_New_Linked_EmployeeDocument_And_Supersedes_Old()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var uploadedBy     = Guid.NewGuid();
        var (_, _, previous) = await Seed(db, companyId, employeeId);
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employeeId, previous.Id, expiryDate: new DateOnly(2030, 1, 1)),
            uploadedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(previous.Id, result.Value!.PreviousVersionId);

        var newVersion = await db.EmployeeDocuments.SingleAsync(ed => ed.Id == result.Value.EmployeeDocumentId);
        Assert.Equal(previous.Id, newVersion.PreviousVersionId);
        Assert.True(newVersion.IsLatestVersion);
        Assert.Equal(new DateOnly(2030, 1, 1), newVersion.ExpiryDate);

        var reloadedPrevious = await db.EmployeeDocuments.SingleAsync(ed => ed.Id == previous.Id);
        Assert.False(reloadedPrevious.IsLatestVersion);

        // Title/description carried forward from the previous version's Document.
        var newDocument = await db.Documents.SingleAsync(d => d.Id == newVersion.DocumentId);
        Assert.Equal("Passport", newDocument.Title);
        Assert.Equal("Some description", newDocument.Description);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_EmployeeDocumentId_Does_Not_Exist()
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
    public async Task HandleAsync_Returns_Conflict_When_Previous_Is_Not_Latest_Version()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var (_, _, previous) = await Seed(db, companyId, employeeId);
        previous.SupersedeAsPreviousVersion(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employeeId, previous.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Previous_Is_Archived()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var (_, _, previous) = await Seed(db, companyId, employeeId);
        previous.Archive(Guid.NewGuid(), "no longer needed", DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employeeId, previous.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Marks_Outstanding_DocumentRequest_Uploaded_Completes_Task_And_Publishes_Fulfilled_Audit()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var uploadedBy     = Guid.NewGuid();
        var (docType, _, previous) = await Seed(db, companyId, employeeId);

        var request = DocumentRequest.Create(
            Guid.NewGuid(), companyId, employeeId, docType.Id, null, null, true, null, null,
            DateTimeOffset.UtcNow);
        db.DocumentRequests.Add(request);
        await db.SaveChangesAsync();

        var taskCompleter = new FakeTaskCompleter();
        var audit          = new FakeAuditPublisher();
        var handler        = BuildHandler(db, taskCompleter: taskCompleter, auditPublisher: audit);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employeeId, previous.Id),
            uploadedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var reloadedRequest = await db.DocumentRequests.SingleAsync(r => r.Id == request.Id);
        Assert.Equal(DocumentRequestStatus.Uploaded, reloadedRequest.Status);

        Assert.Single(taskCompleter.Calls);
        Assert.Equal(request.Id, taskCompleter.Calls[0].SourceEntityId);

        Assert.Contains(audit.Published, e => e.EventType == "document_request.fulfilled");
        Assert.Contains(audit.Published, e => e.EventType == "employee_document.version_uploaded");
    }

    [Fact]
    public async Task HandleAsync_No_Outstanding_Request_Does_Not_Publish_DocumentRequestFulfilled_Audit()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var (_, _, previous) = await Seed(db, companyId, employeeId);

        var taskCompleter = new FakeTaskCompleter();
        var audit          = new FakeAuditPublisher();
        var handler        = BuildHandler(db, taskCompleter: taskCompleter, auditPublisher: audit);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employeeId, previous.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(taskCompleter.Calls);
        Assert.DoesNotContain(audit.Published, e => e.EventType == "document_request.fulfilled");
    }

    [Fact]
    public async Task HandleAsync_New_Version_Has_Null_ExpiryReminderSentAt_Fields()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var (_, _, previous) = await Seed(db, companyId, employeeId);

        // Prove the previous version having reminders already sent does NOT carry over.
        previous.MarkExpiryReminderSent(ExpiryReminderStage.NinetyDays, DateTimeOffset.UtcNow);
        previous.MarkExpiryReminderSent(ExpiryReminderStage.ThirtyDays, DateTimeOffset.UtcNow);
        previous.MarkExpiryReminderSent(ExpiryReminderStage.SevenDays, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employeeId, previous.Id, expiryDate: new DateOnly(2030, 1, 1)),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var newVersion = await db.EmployeeDocuments.SingleAsync(ed => ed.Id == result.Value!.EmployeeDocumentId);
        Assert.Null(newVersion.ExpiryReminder90SentAt);
        Assert.Null(newVersion.ExpiryReminder30SentAt);
        Assert.Null(newVersion.ExpiryReminder7SentAt);
    }
}
