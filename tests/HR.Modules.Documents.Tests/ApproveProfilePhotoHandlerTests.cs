using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.ApproveProfilePhoto;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class ApproveProfilePhotoHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 12, 10, 0, 0, DateTimeKind.Utc);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static (ApproveProfilePhotoHandler Handler, FakeProfilePhotoStorageService Storage,
        FakeTaskCompleter TaskCompleter, FakeNotificationWriter Notifications, FakeAuditPublisher Audit)
        BuildHandler(DocumentsDbContext db)
    {
        var storage       = new FakeProfilePhotoStorageService();
        var taskCompleter = new FakeTaskCompleter();
        var notifications = new FakeNotificationWriter();
        var audit         = new FakeAuditPublisher();
        var handler = new ApproveProfilePhotoHandler(
            db, storage, taskCompleter, notifications, new FakeClock(FixedUtcNow), audit);
        return (handler, storage, taskCompleter, notifications, audit);
    }

    private static PendingProfilePhoto SeedPendingPhoto(
        DocumentsDbContext db, Guid companyId, Guid employeeId, string storageKey = "pending/key.png",
        string fileName = "pending.png", Guid? uploadedBy = null)
    {
        var pending = PendingProfilePhoto.Create(
            Guid.NewGuid(), companyId, employeeId, fileName, 222, "image/png",
            storageKey, uploadedBy ?? employeeId, DateTimeOffset.UtcNow);
        db.PendingProfilePhotos.Add(pending);
        db.SaveChanges();
        return pending;
    }

    private static EmployeeProfilePhoto SeedCurrentPhoto(
        DocumentsDbContext db, Guid companyId, Guid employeeId, string storageKey = "current/key.png")
    {
        var photo = EmployeeProfilePhoto.Create(
            Guid.NewGuid(), companyId, employeeId, "current.png", 111, "image/png",
            storageKey, employeeId, DateTimeOffset.UtcNow);
        db.EmployeeProfilePhotos.Add(photo);
        db.SaveChanges();
        return photo;
    }

    [Fact]
    public async Task HandleAsync_Promotes_Pending_Photo_To_New_Live_Photo_When_None_Exists()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var reviewerId      = Guid.NewGuid();
        var pending           = SeedPendingPhoto(db, companyId, employeeId, "pending/key.png", "avatar.png");
        var (handler, storage, _, _, _) = BuildHandler(db);

        var result = await handler.HandleAsync(
            new ApproveProfilePhotoRequest(companyId, employeeId),
            reviewerId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var live = await db.EmployeeProfilePhotos.SingleAsync();
        Assert.Equal(companyId,           live.CompanyId);
        Assert.Equal(employeeId,          live.EmployeeId);
        Assert.Equal("avatar.png",        live.FileName);
        Assert.Equal(pending.StorageKey,  live.StorageKey);
        Assert.Equal(pending.UploadedBy,  live.UploadedBy);

        // Reuses the pending photo's storage key rather than re-uploading.
        Assert.Empty(storage.Uploads);

        Assert.Equal(live.Id, result.Value!.Id);
        Assert.Contains(pending.StorageKey, result.Value.DownloadUrl);
    }

    [Fact]
    public async Task HandleAsync_Replaces_Existing_Live_Photo_And_Deletes_Old_Blob_Reusing_Pending_StorageKey()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var reviewerId      = Guid.NewGuid();
        var existing          = SeedCurrentPhoto(db, companyId, employeeId, "current/old.png");
        var pending            = SeedPendingPhoto(db, companyId, employeeId, "pending/new.png", "new.png");
        var (handler, storage, _, _, _) = BuildHandler(db);

        var result = await handler.HandleAsync(
            new ApproveProfilePhotoRequest(companyId, employeeId),
            reviewerId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var live = await db.EmployeeProfilePhotos.SingleAsync();
        Assert.Equal(existing.Id,        live.Id); // same row, replaced in place
        Assert.Equal("new.png",          live.FileName);
        Assert.Equal(pending.StorageKey, live.StorageKey);

        // Never re-uploads — reuses the pending submission's blob.
        Assert.Empty(storage.Uploads);

        // Old blob deleted only once the replacement is safely persisted.
        Assert.Single(storage.Deletions);
        Assert.Equal("current/old.png", storage.Deletions[0]);
    }

    [Fact]
    public async Task HandleAsync_Deletes_Pending_Row_After_Promotion()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        SeedPendingPhoto(db, companyId, employeeId);
        var (handler, _, _, _, _) = BuildHandler(db);

        await handler.HandleAsync(
            new ApproveProfilePhotoRequest(companyId, employeeId),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Empty(await db.PendingProfilePhotos.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Completes_Task_Via_TaskCompleter()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var reviewerId      = Guid.NewGuid();
        var pending           = SeedPendingPhoto(db, companyId, employeeId);
        var (handler, _, taskCompleter, _, _) = BuildHandler(db);

        await handler.HandleAsync(
            new ApproveProfilePhotoRequest(companyId, employeeId),
            reviewerId,
            CancellationToken.None);

        var call = Assert.Single(taskCompleter.Calls);
        Assert.Equal(companyId,             call.CompanyId);
        Assert.Equal(pending.Id,            call.SourceEntityId);
        Assert.Equal(TaskSource.Document,   call.Source);
        Assert.Equal(TaskActionType.Review, call.ActionType);
        Assert.Equal(reviewerId,            call.CompletedBy);
    }

    [Fact]
    public async Task HandleAsync_Writes_ProfilePhotoApproved_Notification()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        SeedPendingPhoto(db, companyId, employeeId);
        var (handler, _, _, notifications, _) = BuildHandler(db);

        await handler.HandleAsync(
            new ApproveProfilePhotoRequest(companyId, employeeId),
            Guid.NewGuid(),
            CancellationToken.None);

        var notification = Assert.Single(notifications.Written);
        Assert.Equal(companyId,  notification.CompanyId);
        Assert.Equal(employeeId, notification.EmployeeId);
        Assert.Equal(NotificationType.ProfilePhotoApproved, notification.Type);
    }

    [Fact]
    public async Task HandleAsync_Publishes_ProfilePhotoApproved_Audit_Event()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var reviewerId      = Guid.NewGuid();
        SeedPendingPhoto(db, companyId, employeeId, "pending/key.png", "avatar.png");
        var (handler, _, _, _, audit) = BuildHandler(db);

        var result = await handler.HandleAsync(
            new ApproveProfilePhotoRequest(companyId, employeeId),
            reviewerId,
            CancellationToken.None);

        var evt = Assert.Single(audit.Published.OfType<ProfilePhotoApprovedAuditEvent>());
        Assert.Equal(companyId,          evt.CompanyId);
        Assert.Equal(employeeId,         evt.EmployeeId);
        Assert.Equal(result.Value!.Id,   evt.EmployeeProfilePhotoId);
        Assert.Equal("avatar.png",       evt.FileName);
        Assert.Equal(reviewerId,         evt.ReviewedBy);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_No_Pending_Photo_To_Approve()
    {
        await using var db = BuildContext();
        var (handler, storage, taskCompleter, notifications, audit) = BuildHandler(db);

        var result = await handler.HandleAsync(
            new ApproveProfilePhotoRequest(Guid.NewGuid(), Guid.NewGuid()),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(storage.Deletions);
        Assert.Empty(taskCompleter.Calls);
        Assert.Empty(notifications.Written);
        Assert.Empty(audit.Published);
    }
}
