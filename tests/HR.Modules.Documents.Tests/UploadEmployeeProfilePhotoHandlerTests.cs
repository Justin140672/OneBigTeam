using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.UploadEmployeeProfilePhoto;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Modules.Documents.Tests;

public class UploadEmployeeProfilePhotoHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 12, 10, 0, 0, DateTimeKind.Utc);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static UploadEmployeeProfilePhotoHandler BuildHandler(
        DocumentsDbContext db,
        FakeProfilePhotoStorageService? storage = null,
        ImageUploadOptions? options = null,
        FakeAuditPublisher? auditPublisher = null,
        Hangfire.IBackgroundJobClient? backgroundJobClient = null) =>
        new(db,
            storage ?? new FakeProfilePhotoStorageService(),
            new ImageUploadValidator(Options.Create(options ?? new ImageUploadOptions())),
            new FakeClock(FixedUtcNow),
            auditPublisher ?? new FakeAuditPublisher(),
            backgroundJobClient ?? new NoOpBackgroundJobClient());

    // Produces a valid PNG file with real IHDR dimensions so magic-byte and dimension
    // validation both pass by default.
    private static IFormFile FakePngFile(
        string fileName = "avatar.png",
        int width = 400,
        int height = 300) =>
        FakeFile(fileName, "image/png", ImageTestBytes.BuildPng(width, height));

    private static IFormFile FakeFile(
        string fileName,
        string contentType,
        byte[] content) =>
        new FormFile(new MemoryStream(content), 0, content.Length, "File", fileName)
        {
            Headers     = new HeaderDictionary(),
            ContentType = contentType,
        };

    private static UploadEmployeeProfilePhotoRequest BuildRequest(
        Guid companyId,
        Guid employeeId,
        IFormFile? file = null) =>
        new()
        {
            CompanyId  = companyId,
            EmployeeId = employeeId,
            File       = file ?? FakePngFile(),
        };

    [Fact]
    public async Task HandleAsync_FirstUpload_CreatesNewProfilePhoto_And_ReturnsDownloadUrl()
    {
        await using var db = BuildContext();
        var storage        = new FakeProfilePhotoStorageService();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var uploadedBy     = employeeId; // self-upload
        var handler        = BuildHandler(db, storage);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employeeId),
            uploadedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(companyId,  result.Value!.CompanyId);
        Assert.Equal(employeeId, result.Value.EmployeeId);
        Assert.Equal("avatar.png", result.Value.FileName);
        Assert.Equal("image/png", result.Value.ContentType);
        Assert.NotEqual(Guid.Empty, result.Value.Id);

        var saved = await db.EmployeeProfilePhotos.SingleAsync();
        Assert.Equal(companyId,  saved.CompanyId);
        Assert.Equal(employeeId, saved.EmployeeId);
        Assert.Equal("avatar.png", saved.FileName);
        Assert.Equal(uploadedBy, saved.UploadedBy);

        Assert.Single(storage.Uploads);
        Assert.Equal("avatar.png", storage.Uploads[0].FileName);
        Assert.Contains(storage.Uploads[0].StorageKey, result.Value.DownloadUrl);
    }

    [Fact]
    public async Task HandleAsync_ReUpload_Replaces_Existing_Photo_Instead_Of_Creating_Second_Row()
    {
        await using var db = BuildContext();
        var storage        = new FakeProfilePhotoStorageService();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var handler        = BuildHandler(db, storage);

        var firstResult = await handler.HandleAsync(
            BuildRequest(companyId, employeeId, FakePngFile("first.png")),
            employeeId,
            CancellationToken.None);
        Assert.True(firstResult.IsSuccess);

        var secondResult = await handler.HandleAsync(
            BuildRequest(companyId, employeeId, FakePngFile("second.png")),
            employeeId,
            CancellationToken.None);
        Assert.True(secondResult.IsSuccess);

        // Same logical photo row, replaced in place.
        Assert.Equal(firstResult.Value!.Id, secondResult.Value!.Id);

        var rows = await db.EmployeeProfilePhotos.Where(p => p.EmployeeId == employeeId).ToListAsync();
        Assert.Single(rows);
        Assert.Equal("second.png", rows[0].FileName);

        // Old blob is best-effort deleted once the replacement is safely persisted.
        Assert.Equal(2, storage.Uploads.Count);
        Assert.Single(storage.Deletions);
        Assert.Equal(storage.Uploads[0].StorageKey, storage.Deletions[0]);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_File_Too_Large_And_Does_Not_Touch_Storage_Or_Db()
    {
        await using var db = BuildContext();
        var storage         = new FakeProfilePhotoStorageService();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var handler          = BuildHandler(db, storage, options: new ImageUploadOptions { MaxFileSizeBytes = 10 });

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employeeId),
            employeeId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("size", result.Error.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Empty(storage.Uploads);
        Assert.Empty(await db.EmployeeProfilePhotos.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Extension_Not_Allowed()
    {
        await using var db = BuildContext();
        var storage         = new FakeProfilePhotoStorageService();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var handler          = BuildHandler(db, storage);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employeeId,
                FakeFile("avatar.gif", "image/gif", ImageTestBytes.BuildPng(400, 300))),
            employeeId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(storage.Uploads);
    }

    [Fact]
    public async Task HandleAsync_Stores_Photo_With_Pending_ScanStatus()
    {
        // Virus scanning now happens asynchronously (ScanUploadedFileJob, enqueued after
        // persistence) rather than inline during upload.
        await using var db = BuildContext();
        var storage        = new FakeProfilePhotoStorageService();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var handler        = BuildHandler(db, storage);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employeeId),
            employeeId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = await db.EmployeeProfilePhotos.SingleAsync(p => p.Id == result.Value!.Id);
        Assert.Equal(FileScanStatus.Pending, stored.ScanStatus);
    }

    [Fact]
    public async Task HandleAsync_Enqueues_ScanUploadedFileJob_On_Success()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var backgroundJobs = new SpyBackgroundJobClient();
        var handler         = BuildHandler(db, backgroundJobClient: backgroundJobs);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employeeId),
            employeeId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(backgroundJobs.CreatedJobs);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Magic_Bytes_Do_Not_Match_ContentType()
    {
        await using var db = BuildContext();
        var storage         = new FakeProfilePhotoStorageService();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var handler          = BuildHandler(db, storage);

        // Extension/content type say PNG, but bytes are zeros (renamed/spoofed file).
        var spoofedFile = FakeFile("legit.png", "image/png", new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employeeId, spoofedFile),
            employeeId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("content does not match", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(storage.Uploads);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Dimensions_Below_Minimum_And_Does_Not_Upload()
    {
        await using var db = BuildContext();
        var storage         = new FakeProfilePhotoStorageService();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var handler          = BuildHandler(db, storage);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employeeId, FakePngFile(width: 10, height: 10)),
            employeeId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("smaller than the minimum", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(storage.Uploads);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Dimensions_Above_Maximum_And_Does_Not_Upload()
    {
        await using var db = BuildContext();
        var storage         = new FakeProfilePhotoStorageService();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var handler          = BuildHandler(db, storage);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, employeeId, FakePngFile(width: 5000, height: 5000)),
            employeeId,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("exceed the maximum", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(storage.Uploads);
    }

    [Fact]
    public async Task HandleAsync_Publishes_ProfilePhotoUploaded_Audit_Event_With_IsReplace_False_On_First_Upload()
    {
        await using var db = BuildContext();
        var audit          = new FakeAuditPublisher();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var handler        = BuildHandler(db, auditPublisher: audit);

        await handler.HandleAsync(
            BuildRequest(companyId, employeeId),
            employeeId,
            CancellationToken.None);

        var evt = Assert.Single(audit.Published);
        Assert.Equal("profile_photo.uploaded", evt.EventType);
        Assert.Equal("EmployeeProfilePhoto",   evt.EntityType);
        Assert.Equal(companyId,  evt.CompanyId);
        Assert.Equal(employeeId, evt.EmployeeId);
        Assert.Equal(employeeId, evt.ActorUserId);
        Assert.Equal(employeeId, evt.ActorEmployeeId); // self-upload
    }

    [Fact]
    public async Task HandleAsync_Publishes_ProfilePhotoUploaded_Audit_Event_With_IsReplace_True_On_Second_Upload()
    {
        await using var db = BuildContext();
        var audit          = new FakeAuditPublisher();
        var companyId      = Guid.NewGuid();
        var employeeId     = Guid.NewGuid();
        var managerId      = Guid.NewGuid();
        var handler        = BuildHandler(db, auditPublisher: audit);

        await handler.HandleAsync(
            BuildRequest(companyId, employeeId, FakePngFile("first.png")),
            employeeId,
            CancellationToken.None);

        await handler.HandleAsync(
            BuildRequest(companyId, employeeId, FakePngFile("second.png")),
            managerId,
            CancellationToken.None);

        Assert.Equal(2, audit.Published.Count);

        var firstEvt  = audit.Published[0];
        var secondEvt = audit.Published[1];

        Assert.Equal(employeeId, firstEvt.ActorUserId);
        Assert.Equal(employeeId, firstEvt.ActorEmployeeId); // self-upload → not a manager upload

        Assert.Equal(managerId, secondEvt.ActorUserId);
        Assert.Null(secondEvt.ActorEmployeeId); // manager upload → actor is a user, not an employee
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_When_Metadata_Validation_Fails()
    {
        await using var db = BuildContext();
        var audit          = new FakeAuditPublisher();
        var handler        = BuildHandler(db, options: new ImageUploadOptions { MaxFileSizeBytes = 1 }, auditPublisher: audit);

        await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid()),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Empty(audit.Published);
    }

    [Fact]
    public async Task HandleAsync_Deletes_StorageObject_When_DbSave_Fails()
    {
        var storage    = new FakeProfilePhotoStorageService();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ThrowingDocumentsDbContext(options);

        var handler = BuildHandler(db, storage);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            handler.HandleAsync(
                BuildRequest(companyId, employeeId),
                employeeId,
                CancellationToken.None));

        // The file that was uploaded must have been cleaned up.
        Assert.Single(storage.Uploads);
        Assert.Single(storage.Deletions);
        Assert.Equal(storage.Uploads[0].StorageKey, storage.Deletions[0]);
    }

    // Subclass used only in the orphan-cleanup test to simulate a DB save failure.
    private sealed class ThrowingDocumentsDbContext(DbContextOptions<DocumentsDbContext> options)
        : DocumentsDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("Simulated database failure.");
    }
}
