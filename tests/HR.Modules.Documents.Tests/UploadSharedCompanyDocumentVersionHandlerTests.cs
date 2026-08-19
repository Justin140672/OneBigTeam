using HR.Modules.Tasks.Contracts;
using System.Text.Json;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.UploadSharedCompanyDocumentVersion;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Modules.Documents.Tests;

public class UploadSharedCompanyDocumentVersionHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTime FixedUtcNow = new(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset FixedNowOffset = new(FixedUtcNow, TimeSpan.Zero);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static UploadSharedCompanyDocumentVersionHandler BuildHandler(
        DocumentsDbContext db,
        FakeDocumentStorageService? storage = null,
        FakeEmployeeAudienceReader? audienceReader = null,
        FakeTaskCreator? taskCreator = null,
        FakeTaskCanceller? taskCanceller = null,
        FakeNotificationWriter? notificationWriter = null,
        FileUploadOptions? options = null,
        FakeAuditPublisher? auditPublisher = null,
        Hangfire.IBackgroundJobClient? backgroundJobClient = null) =>
        new(db,
            storage ?? new FakeDocumentStorageService(),
            new FileUploadValidator(Options.Create(options ?? new FileUploadOptions())),
            new SharedCompanyDocumentAudienceMatcher(db, audienceReader ?? new FakeEmployeeAudienceReader()),
            taskCreator ?? new FakeTaskCreator(),
            taskCanceller ?? new FakeTaskCanceller(),
            notificationWriter ?? new FakeNotificationWriter(),
            auditPublisher ?? new FakeAuditPublisher(),
            new FakeClock(FixedUtcNow),
            backgroundJobClient ?? new NoOpBackgroundJobClient());

    private static async Task<CompanyDocumentCategory> SeedCategory(
        DocumentsDbContext db, Guid companyId, string name = "Policy")
    {
        var category = CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, name, Now);
        db.CompanyDocumentCategories.Add(category);
        await db.SaveChangesAsync();
        return category;
    }

    private static async Task<SharedCompanyDocument> SeedDocument(
        DocumentsDbContext db,
        Guid companyId,
        Guid categoryId,
        bool requiresAcknowledgement = false,
        bool published = false,
        Guid? createdBy = null)
    {
        var owner = createdBy ?? Guid.NewGuid();
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Remote Working Policy", null, categoryId,
            "key/v1.pdf", "v1.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null,
            requiresAcknowledgement,
            requiresAcknowledgement ? new DateOnly(2027, 1, 1) : null,
            null,
            owner, Now);

        if (published)
            doc.Publish(Guid.NewGuid(), Now);

        db.SharedCompanyDocuments.Add(doc);
        db.SharedCompanyDocumentVersions.Add(SharedCompanyDocumentVersion.Create(
            Guid.NewGuid(), companyId, doc.Id, 1, "key/v1.pdf", "v1.pdf", 100, "application/pdf",
            owner, Now, versionNote: null, requiresAcknowledgement: requiresAcknowledgement, effectiveDate: null));
        await db.SaveChangesAsync();
        return doc;
    }

    // Produces a PDF file with valid magic bytes so magic-byte validation passes.
    private static IFormFile FakePdfFile(string fileName = "policy-v2.pdf", int extraSize = 1020) =>
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

    private static UploadSharedCompanyDocumentVersionRequest BuildRequest(
        Guid companyId,
        Guid documentId,
        IFormFile? file = null,
        string versionNote = "Updated section 3",
        bool requiresReacknowledgement = false,
        string? acknowledgementStatement = null) =>
        new()
        {
            CompanyId                 = companyId,
            DocumentId                = documentId,
            VersionNote               = versionNote,
            RequiresReacknowledgement = requiresReacknowledgement,
            AcknowledgementStatement  = acknowledgementStatement,
            File                      = file ?? FakePdfFile(),
        };

    [Fact]
    public async Task HandleAsync_Increments_VersionNumber_And_Preserves_Original_Version_Row()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var category   = await SeedCategory(db, companyId);
        var doc        = await SeedDocument(db, companyId, category.Id);
        var uploadedBy = Guid.NewGuid();
        var handler    = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, doc.Id, file: FakePdfFile("policy-v2.pdf"), versionNote: "Updated section 3"),
            uploadedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.VersionNumber);
        Assert.Equal("policy-v2.pdf", result.Value.FileName);
        Assert.Equal("Updated section 3", result.Value.VersionNote);

        var stored = await db.SharedCompanyDocuments.AsNoTracking().SingleAsync(d => d.Id == doc.Id);
        Assert.Equal(2, stored.VersionNumber);
        Assert.Equal("policy-v2.pdf", stored.FileName);

        var versions = await db.SharedCompanyDocumentVersions
            .Where(v => v.SharedCompanyDocumentId == doc.Id)
            .ToListAsync();
        Assert.Equal(2, versions.Count);

        var v1 = versions.Single(v => v.VersionNumber == 1);
        Assert.Equal("v1.pdf", v1.FileName);

        var v2 = versions.Single(v => v.VersionNumber == 2);
        Assert.Equal("policy-v2.pdf", v2.FileName);
        Assert.Equal("Updated section 3", v2.VersionNote);
    }

    [Fact]
    public async Task HandleAsync_Sets_CreatedBy_And_CreatedAt_On_New_Version_Row()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var category   = await SeedCategory(db, companyId);
        var doc        = await SeedDocument(db, companyId, category.Id);
        var uploadedBy = Guid.NewGuid();
        var handler    = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, doc.Id),
            uploadedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(uploadedBy, result.Value!.UploadedBy);
        Assert.Equal(FixedNowOffset, result.Value.UploadedAt);

        var newVersion = await db.SharedCompanyDocumentVersions
            .SingleAsync(v => v.SharedCompanyDocumentId == doc.Id && v.VersionNumber == 2);
        Assert.Equal(uploadedBy, newVersion.CreatedBy);
        Assert.Equal(FixedNowOffset, newVersion.CreatedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Unknown_Document()
    {
        await using var db = BuildContext();
        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid()),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Stores_New_Version_With_Pending_ScanStatus()
    {
        // Virus scanning now happens asynchronously (ScanUploadedFileJob, enqueued after
        // persistence) rather than inline during upload.
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var category   = await SeedCategory(db, companyId);
        var doc        = await SeedDocument(db, companyId, category.Id);
        var handler    = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, doc.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = await db.SharedCompanyDocumentVersions
            .SingleAsync(v => v.SharedCompanyDocumentId == doc.Id && v.VersionNumber == result.Value!.VersionNumber);
        Assert.Equal(FileScanStatus.Pending, stored.ScanStatus);
    }

    [Fact]
    public async Task HandleAsync_Enqueues_ScanUploadedFileJob_On_Success()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var category   = await SeedCategory(db, companyId);
        var doc        = await SeedDocument(db, companyId, category.Id);
        var backgroundJobs = new SpyBackgroundJobClient();
        var handler          = BuildHandler(db, backgroundJobClient: backgroundJobs);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, doc.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Uploading a new version enqueues a scan job for both the document (whose
        // CurrentFileReference/ScanStatus were just reset to Pending) and the new version row.
        Assert.Equal(2, backgroundJobs.CreatedJobs.Count);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Document_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var category = await SeedCategory(db, companyA);
        var doc      = await SeedDocument(db, companyA, category.Id);
        var handler  = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), doc.Id), // different company in the request
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Document_Archived()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc       = await SeedDocument(db, companyId, category.Id, published: true);
        var stored    = await db.SharedCompanyDocuments.SingleAsync(d => d.Id == doc.Id);
        stored.Archive(Guid.NewGuid(), "Superseded", Now);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, doc.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Creates_Acknowledgement_Tasks_For_Every_Eligible_Employee_When_Reacknowledgement_Required()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc       = await SeedDocument(db, companyId, category.Id, requiresAcknowledgement: true, published: true);
        var emp1 = Guid.NewGuid();
        var emp2 = Guid.NewGuid();

        var audienceReader     = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [emp1, emp2] };
        var taskCreator        = new FakeTaskCreator();
        var notificationWriter = new FakeNotificationWriter();
        var handler             = BuildHandler(
            db, audienceReader: audienceReader, taskCreator: taskCreator, notificationWriter: notificationWriter);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, doc.Id, requiresReacknowledgement: true),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, taskCreator.Created.Count);
        Assert.Contains(taskCreator.Created, t => t.AssignedEmployeeId == emp1);
        Assert.Contains(taskCreator.Created, t => t.AssignedEmployeeId == emp2);
        Assert.All(taskCreator.Created, t => Assert.Equal("Acknowledge: Remote Working Policy (v2)", t.Title));

        Assert.Equal(2, notificationWriter.Written.Count);
        Assert.All(notificationWriter.Written, n =>
        {
            Assert.Equal(NotificationType.SharedCompanyDocumentAcknowledgementReminder, n.Type);
            Assert.Equal(doc.Id, n.SourceEntityId);
        });
        Assert.Contains(notificationWriter.Written, n => n.EmployeeId == emp1);
        Assert.Contains(notificationWriter.Written, n => n.EmployeeId == emp2);
    }

    [Fact]
    public async Task HandleAsync_Carries_Forward_Prior_Acknowledgement_When_Reacknowledgement_Not_Required()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc       = await SeedDocument(db, companyId, category.Id, requiresAcknowledgement: true, published: true);
        var employeeId = Guid.NewGuid();
        var originalAcknowledgedAt = Now.AddDays(-3);

        db.SharedCompanyDocumentAcknowledgements.Add(SharedCompanyDocumentAcknowledgement.Create(
            Guid.NewGuid(), companyId, doc.Id, employeeId, 1, "Original statement", null, true, originalAcknowledgedAt));
        await db.SaveChangesAsync();

        var taskCreator        = new FakeTaskCreator();
        var notificationWriter = new FakeNotificationWriter();
        var handler = BuildHandler(db, taskCreator: taskCreator, notificationWriter: notificationWriter);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, doc.Id, requiresReacknowledgement: false),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(taskCreator.Created);
        Assert.Empty(notificationWriter.Written);

        var carriedForward = await db.SharedCompanyDocumentAcknowledgements
            .SingleAsync(a => a.SharedCompanyDocumentId == doc.Id && a.VersionNumber == 2 && a.EmployeeId == employeeId);
        Assert.Equal("Original statement", carriedForward.AcknowledgementStatement);
        Assert.Equal(originalAcknowledgedAt, carriedForward.AcknowledgedAt);

        var original = await db.SharedCompanyDocumentAcknowledgements
            .SingleAsync(a => a.SharedCompanyDocumentId == doc.Id && a.VersionNumber == 1 && a.EmployeeId == employeeId);
        Assert.Equal(originalAcknowledgedAt, original.AcknowledgedAt);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Carry_Forward_Or_Create_Task_For_Employee_Who_Never_Acknowledged_Prior_Version()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc       = await SeedDocument(db, companyId, category.Id, requiresAcknowledgement: true, published: true);
        var neverAcknowledged = Guid.NewGuid();

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [neverAcknowledged] };
        var taskCreator     = new FakeTaskCreator();
        var handler         = BuildHandler(db, audienceReader: audienceReader, taskCreator: taskCreator);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, doc.Id, requiresReacknowledgement: false),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(taskCreator.Created);

        var newVersionAcknowledgements = await db.SharedCompanyDocumentAcknowledgements
            .Where(a => a.SharedCompanyDocumentId == doc.Id && a.VersionNumber == 2)
            .ToListAsync();
        Assert.Empty(newVersionAcknowledgements);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HandleAsync_Does_Nothing_Acknowledgement_Related_When_Document_Does_Not_Require_Acknowledgement(
        bool requiresReacknowledgement)
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc       = await SeedDocument(db, companyId, category.Id, requiresAcknowledgement: false, published: true);

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [Guid.NewGuid()] };
        var taskCreator     = new FakeTaskCreator();
        var handler         = BuildHandler(db, audienceReader: audienceReader, taskCreator: taskCreator);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, doc.Id, requiresReacknowledgement: requiresReacknowledgement),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.VersionNumber);
        Assert.Empty(taskCreator.Created);
        Assert.Empty(await db.SharedCompanyDocumentAcknowledgements.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Create_Tasks_For_Draft_Document_Even_When_Acknowledgement_Required()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        // never published — stays Draft
        var doc = await SeedDocument(db, companyId, category.Id, requiresAcknowledgement: true, published: false);

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [Guid.NewGuid()] };
        var taskCreator     = new FakeTaskCreator();
        var handler         = BuildHandler(db, audienceReader: audienceReader, taskCreator: taskCreator);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, doc.Id, requiresReacknowledgement: true),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.VersionNumber);
        Assert.Empty(taskCreator.Created);

        var versionRow = await db.SharedCompanyDocumentVersions
            .SingleAsync(v => v.SharedCompanyDocumentId == doc.Id && v.VersionNumber == 2);
        Assert.NotNull(versionRow);
    }

    [Fact]
    public async Task HandleAsync_Never_Mutates_Original_Acknowledgement_Rows_For_Old_Version()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc       = await SeedDocument(db, companyId, category.Id, requiresAcknowledgement: true, published: true);
        var employeeId = Guid.NewGuid();
        var originalAcknowledgedAt = Now.AddDays(-5);

        db.SharedCompanyDocumentAcknowledgements.Add(SharedCompanyDocumentAcknowledgement.Create(
            Guid.NewGuid(), companyId, doc.Id, employeeId, 1, "Original statement", null, true, originalAcknowledgedAt));
        await db.SaveChangesAsync();

        // Reacknowledgement required this time — exercises the task-creation branch, which also
        // must never touch the version-1 row.
        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [employeeId] };
        var handler = BuildHandler(db, audienceReader: audienceReader);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, doc.Id, requiresReacknowledgement: true),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var original = await db.SharedCompanyDocumentAcknowledgements
            .SingleAsync(a => a.SharedCompanyDocumentId == doc.Id && a.VersionNumber == 1 && a.EmployeeId == employeeId);
        Assert.Equal(1, original.VersionNumber);
        Assert.Equal("Original statement", original.AcknowledgementStatement);
        Assert.Equal(originalAcknowledgedAt, original.AcknowledgedAt);
    }

    [Fact]
    public async Task HandleAsync_Deletes_StorageObject_When_DbSave_Fails()
    {
        var companyId  = Guid.NewGuid();
        var uploadedBy = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ThrowingDocumentsDbContext(options);
        var category = CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, "Policy", Now);
        db.CompanyDocumentCategories.Add(category);
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/v1.pdf", "v1.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, false, null, null, Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.BaseSaveChangesAsync(); // seed without throwing

        var storage = new FakeDocumentStorageService();
        var handler = BuildHandler(db, storage: storage);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            handler.HandleAsync(
                BuildRequest(companyId, doc.Id),
                uploadedBy,
                CancellationToken.None));

        Assert.Single(storage.Uploads);
        Assert.Single(storage.Deletions);
        Assert.Equal(storage.Uploads[0].StorageKey, storage.Deletions[0]);
    }

    [Fact]
    public async Task HandleAsync_Publishes_VersionUploaded_Audit_Event_On_Success()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var category        = await SeedCategory(db, companyId);
        var doc              = await SeedDocument(db, companyId, category.Id);
        var uploadedBy       = Guid.NewGuid();
        var storage          = new FakeDocumentStorageService();
        var auditPublisher   = new FakeAuditPublisher();
        var handler          = BuildHandler(db, storage: storage, auditPublisher: auditPublisher);

        var result = await handler.HandleAsync(
            BuildRequest(
                companyId, doc.Id,
                file: FakePdfFile("policy-v2.pdf"),
                versionNote: "Updated section 3",
                requiresReacknowledgement: true),
            uploadedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var published = Assert.Single(auditPublisher.Published.OfType<SharedCompanyDocumentVersionUploadedAuditEvent>());
        Assert.Equal(companyId,               published.CompanyId);
        Assert.Equal(doc.Id,                  published.SharedCompanyDocumentId);
        Assert.Equal("Remote Working Policy", published.Title);
        Assert.Equal("policy-v2.pdf",         published.FileName);
        Assert.Equal(result.Value!.FileSize,  published.FileSize);
        Assert.Equal(2,                       published.VersionNumber);
        Assert.Equal("Updated section 3",     published.VersionNote);
        Assert.True(published.RequiresReacknowledgement);
        Assert.Equal(uploadedBy,              published.UploadedBy);

        // Safety: the raw storage key must never appear in any published audit event's field
        // values — only FileName, FileSize, VersionNumber, VersionNote and identifiers are
        // recorded, never the storage key or a signed download URL.
        var storageKey = storage.Uploads[0].StorageKey;
        Assert.NotEqual(storageKey, published.FileName);
        Assert.All(auditPublisher.Published, evt =>
        {
            var afterJson = JsonSerializer.Serialize(evt.After);
            Assert.DoesNotContain(storageKey, afterJson, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task HandleAsync_WithRequiresReacknowledgement_CancelsPriorVersionsOpenAcknowledgeTasks()
    {
        // Regression test: an employee who hadn't yet acknowledged the previous version used to
        // end up with two open Acknowledge tasks for the same document once a new version
        // requiring re-acknowledgement was uploaded — ITaskCanceller.CancelAllBySourceEntityAsync
        // now cancels every still-open task for this document before the new ones are created.
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var category   = await SeedCategory(db, companyId);
        var doc         = await SeedDocument(db, companyId, category.Id, requiresAcknowledgement: true, published: true);
        var uploadedBy  = Guid.NewGuid();
        var taskCanceller = new FakeTaskCanceller { CancelAllReturnCount = 1 };
        var handler       = BuildHandler(db, taskCanceller: taskCanceller);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, doc.Id, requiresReacknowledgement: true),
            uploadedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var cancelCall = Assert.Single(taskCanceller.CancelAllCalls);
        Assert.Equal(companyId, cancelCall.CompanyId);
        Assert.Equal(doc.Id,    cancelCall.SourceEntityId);
        Assert.Equal(TaskSource.Document,       cancelCall.Source);
        Assert.Equal(TaskActionType.Acknowledge, cancelCall.ActionType);
    }

    [Fact]
    public async Task HandleAsync_Copies_Forward_Previous_Version_AcknowledgementStatement_When_No_Override_Given()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = await SeedDocument(db, companyId, category.Id, requiresAcknowledgement: true, published: true);

        var v1 = await db.SharedCompanyDocumentVersions.SingleAsync(v => v.SharedCompanyDocumentId == doc.Id && v.VersionNumber == 1);
        // Domain type has no public setter for AcknowledgementStatement outside Create, so seed it
        // via a fresh row replacing the existing one with the value the handler should copy forward.
        db.SharedCompanyDocumentVersions.Remove(v1);
        db.SharedCompanyDocumentVersions.Add(SharedCompanyDocumentVersion.Create(
            Guid.NewGuid(), companyId, doc.Id, 1, "key/v1.pdf", "v1.pdf", 100, "application/pdf",
            Guid.NewGuid(), Now, versionNote: null, requiresAcknowledgement: true, effectiveDate: null,
            acknowledgementStatement: "Original wording from version 1."));
        await db.SaveChangesAsync();

        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, doc.Id, requiresReacknowledgement: true, acknowledgementStatement: null),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var newVersion = await db.SharedCompanyDocumentVersions
            .SingleAsync(v => v.SharedCompanyDocumentId == doc.Id && v.VersionNumber == 2);
        Assert.Equal("Original wording from version 1.", newVersion.AcknowledgementStatement);

        var previousVersion = await db.SharedCompanyDocumentVersions
            .SingleAsync(v => v.SharedCompanyDocumentId == doc.Id && v.VersionNumber == 1);
        Assert.Equal("Original wording from version 1.", previousVersion.AcknowledgementStatement);
    }

    [Fact]
    public async Task HandleAsync_Uses_Request_AcknowledgementStatement_Override_Instead_Of_Previous_Version_When_Provided()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = await SeedDocument(db, companyId, category.Id, requiresAcknowledgement: true, published: true);

        var v1 = await db.SharedCompanyDocumentVersions.SingleAsync(v => v.SharedCompanyDocumentId == doc.Id && v.VersionNumber == 1);
        db.SharedCompanyDocumentVersions.Remove(v1);
        db.SharedCompanyDocumentVersions.Add(SharedCompanyDocumentVersion.Create(
            Guid.NewGuid(), companyId, doc.Id, 1, "key/v1.pdf", "v1.pdf", 100, "application/pdf",
            Guid.NewGuid(), Now, versionNote: null, requiresAcknowledgement: true, effectiveDate: null,
            acknowledgementStatement: "Original wording from version 1."));
        await db.SaveChangesAsync();

        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(
                companyId, doc.Id,
                requiresReacknowledgement: true,
                acknowledgementStatement: "HR-edited wording for version 2."),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var newVersion = await db.SharedCompanyDocumentVersions
            .SingleAsync(v => v.SharedCompanyDocumentId == doc.Id && v.VersionNumber == 2);
        Assert.Equal("HR-edited wording for version 2.", newVersion.AcknowledgementStatement);

        // The previous version's own row must remain untouched — it still shows what was in effect
        // when it was created, independent of this override.
        var previousVersion = await db.SharedCompanyDocumentVersions
            .SingleAsync(v => v.SharedCompanyDocumentId == doc.Id && v.VersionNumber == 1);
        Assert.Equal("Original wording from version 1.", previousVersion.AcknowledgementStatement);
    }

    [Fact]
    public async Task HandleAsync_Updates_AcknowledgementStatement_When_Reacknowledgement_Required_And_Statement_Provided()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = await SeedDocument(db, companyId, category.Id, requiresAcknowledgement: true, published: true);
        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(
                companyId, doc.Id,
                requiresReacknowledgement: true,
                acknowledgementStatement: "I confirm I have read the updated policy."),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var stored = await db.SharedCompanyDocuments.AsNoTracking().SingleAsync(d => d.Id == doc.Id);
        Assert.Equal("I confirm I have read the updated policy.", stored.AcknowledgementStatement);
    }

    [Fact]
    public async Task HandleAsync_Ignores_AcknowledgementStatement_When_Reacknowledgement_Not_Required()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = await SeedDocument(db, companyId, category.Id, requiresAcknowledgement: true, published: true);
        var originalStatement = doc.AcknowledgementStatement;
        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(
                companyId, doc.Id,
                requiresReacknowledgement: false,
                acknowledgementStatement: "This should be ignored."),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var stored = await db.SharedCompanyDocuments.AsNoTracking().SingleAsync(d => d.Id == doc.Id);
        Assert.Equal(originalStatement, stored.AcknowledgementStatement);
        Assert.NotEqual("This should be ignored.", stored.AcknowledgementStatement);
    }

    [Fact]
    public async Task HandleAsync_Keeps_Existing_Statement_When_Reacknowledgement_Required_But_Statement_Omitted()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = await SeedDocument(db, companyId, category.Id, requiresAcknowledgement: true, published: true);
        var originalStatement = doc.AcknowledgementStatement;
        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, doc.Id, requiresReacknowledgement: true, acknowledgementStatement: null),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var stored = await db.SharedCompanyDocuments.AsNoTracking().SingleAsync(d => d.Id == doc.Id);
        Assert.Equal(originalStatement, stored.AcknowledgementStatement);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Set_AcknowledgementStatement_When_Document_Does_Not_Require_Acknowledgement()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = await SeedDocument(db, companyId, category.Id, requiresAcknowledgement: false, published: true);
        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(
                companyId, doc.Id,
                requiresReacknowledgement: true,
                acknowledgementStatement: "Should never be applied."),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var stored = await db.SharedCompanyDocuments.AsNoTracking().SingleAsync(d => d.Id == doc.Id);
        Assert.False(stored.RequiresAcknowledgement);
        Assert.Null(stored.AcknowledgementStatement);
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

public class UploadSharedCompanyDocumentVersionValidatorTests
{
    private readonly UploadSharedCompanyDocumentVersionValidator _validator = new();

    private static IFormFile SomeFile() =>
        new FormFile(new MemoryStream([0x25, 0x50, 0x44, 0x46]), 0, 4, "File", "policy.pdf")
        {
            Headers     = new HeaderDictionary(),
            ContentType = "application/pdf",
        };

    [Fact]
    public void Validate_Passes_For_Valid_Request()
    {
        var result = _validator.Validate(new UploadSharedCompanyDocumentVersionRequest
        {
            CompanyId   = Guid.NewGuid(),
            DocumentId  = Guid.NewGuid(),
            VersionNote = "Updated section 3",
            File        = SomeFile(),
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        var result = _validator.Validate(new UploadSharedCompanyDocumentVersionRequest
        {
            CompanyId   = Guid.Empty,
            DocumentId  = Guid.NewGuid(),
            VersionNote = "Updated section 3",
            File        = SomeFile(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadSharedCompanyDocumentVersionRequest.CompanyId));
    }

    [Fact]
    public void Validate_Fails_When_DocumentId_Is_Empty()
    {
        var result = _validator.Validate(new UploadSharedCompanyDocumentVersionRequest
        {
            CompanyId   = Guid.NewGuid(),
            DocumentId  = Guid.Empty,
            VersionNote = "Updated section 3",
            File        = SomeFile(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadSharedCompanyDocumentVersionRequest.DocumentId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_Fails_When_VersionNote_Is_Empty_Or_Whitespace(string versionNote)
    {
        var result = _validator.Validate(new UploadSharedCompanyDocumentVersionRequest
        {
            CompanyId   = Guid.NewGuid(),
            DocumentId  = Guid.NewGuid(),
            VersionNote = versionNote,
            File        = SomeFile(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadSharedCompanyDocumentVersionRequest.VersionNote));
    }

    [Fact]
    public void Validate_Fails_When_VersionNote_Exceeds_Max_Length()
    {
        var result = _validator.Validate(new UploadSharedCompanyDocumentVersionRequest
        {
            CompanyId   = Guid.NewGuid(),
            DocumentId  = Guid.NewGuid(),
            VersionNote = new string('A', 1001),
            File        = SomeFile(),
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadSharedCompanyDocumentVersionRequest.VersionNote));
    }

    [Fact]
    public void Validate_Fails_When_File_Is_Null()
    {
        var result = _validator.Validate(new UploadSharedCompanyDocumentVersionRequest
        {
            CompanyId   = Guid.NewGuid(),
            DocumentId  = Guid.NewGuid(),
            VersionNote = "Updated section 3",
            File        = null!,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadSharedCompanyDocumentVersionRequest.File));
    }
}

public class SharedCompanyDocumentVersionDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

    private static SharedCompanyDocumentVersion CreateVersion(string? acknowledgementStatement) =>
        SharedCompanyDocumentVersion.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            "key/v1.pdf", "v1.pdf", 100, "application/pdf",
            Guid.NewGuid(), Now,
            versionNote: null,
            requiresAcknowledgement: true,
            effectiveDate: null,
            acknowledgementStatement: acknowledgementStatement);

    [Fact]
    public void Create_Stores_The_AcknowledgementStatement_Trimmed()
    {
        var version = CreateVersion("  Please confirm you have read this.  ");

        Assert.Equal("Please confirm you have read this.", version.AcknowledgementStatement);
    }

    [Fact]
    public void Create_Defaults_AcknowledgementStatement_To_Null_When_Not_Provided()
    {
        var version = SharedCompanyDocumentVersion.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            "key/v1.pdf", "v1.pdf", 100, "application/pdf",
            Guid.NewGuid(), Now,
            versionNote: null,
            requiresAcknowledgement: false,
            effectiveDate: null);

        Assert.Null(version.AcknowledgementStatement);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Normalizes_Null_Or_Whitespace_AcknowledgementStatement_To_Null(string? acknowledgementStatement)
    {
        var version = CreateVersion(acknowledgementStatement);

        Assert.Null(version.AcknowledgementStatement);
    }
}
