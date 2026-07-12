using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.RejectProfilePhoto;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class RejectProfilePhotoHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 12, 10, 0, 0, DateTimeKind.Utc);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static (RejectProfilePhotoHandler Handler, FakeProfilePhotoStorageService Storage,
        FakeTaskCompleter TaskCompleter, FakeNotificationWriter Notifications, FakeAuditPublisher Audit)
        BuildHandler(DocumentsDbContext db)
    {
        var storage       = new FakeProfilePhotoStorageService();
        var taskCompleter = new FakeTaskCompleter();
        var notifications = new FakeNotificationWriter();
        var audit         = new FakeAuditPublisher();
        var handler = new RejectProfilePhotoHandler(
            db, storage, taskCompleter, notifications, new FakeClock(FixedUtcNow), audit);
        return (handler, storage, taskCompleter, notifications, audit);
    }

    private static PendingProfilePhoto SeedPendingPhoto(
        DocumentsDbContext db, Guid companyId, Guid employeeId, string storageKey = "pending/key.png")
    {
        var pending = PendingProfilePhoto.Create(
            Guid.NewGuid(), companyId, employeeId, "pending.png", 222, "image/png",
            storageKey, employeeId, DateTimeOffset.UtcNow);
        db.PendingProfilePhotos.Add(pending);
        db.SaveChanges();
        return pending;
    }

    [Fact]
    public async Task HandleAsync_Deletes_Pending_Photo_And_Blob_Without_Touching_EmployeeProfilePhoto()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var pending           = SeedPendingPhoto(db, companyId, employeeId);
        var (handler, storage, _, _, _) = BuildHandler(db);

        var result = await handler.HandleAsync(
            new RejectProfilePhotoRequest(companyId, employeeId, null),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(await db.PendingProfilePhotos.ToListAsync());
        Assert.Empty(await db.EmployeeProfilePhotos.ToListAsync());

        Assert.Single(storage.Deletions);
        Assert.Equal(pending.StorageKey, storage.Deletions[0]);
    }

    [Fact]
    public async Task HandleAsync_Completes_Task_Rather_Than_Cancelling_It()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var reviewerId      = Guid.NewGuid();
        var pending           = SeedPendingPhoto(db, companyId, employeeId);
        var (handler, _, taskCompleter, _, _) = BuildHandler(db);

        await handler.HandleAsync(
            new RejectProfilePhotoRequest(companyId, employeeId, null),
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
    public async Task HandleAsync_Writes_ProfilePhotoRejected_Notification_With_Reason_In_Body_When_Provided()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        SeedPendingPhoto(db, companyId, employeeId);
        var (handler, _, _, notifications, _) = BuildHandler(db);

        await handler.HandleAsync(
            new RejectProfilePhotoRequest(companyId, employeeId, "Photo is blurry"),
            Guid.NewGuid(),
            CancellationToken.None);

        var notification = Assert.Single(notifications.Written);
        Assert.Equal(companyId,  notification.CompanyId);
        Assert.Equal(employeeId, notification.EmployeeId);
        Assert.Equal(NotificationType.ProfilePhotoRejected, notification.Type);
        Assert.Contains("Photo is blurry", notification.Body);
    }

    [Fact]
    public async Task HandleAsync_Writes_ProfilePhotoRejected_Notification_With_Generic_Body_When_Reason_Omitted()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        SeedPendingPhoto(db, companyId, employeeId);
        var (handler, _, _, notifications, _) = BuildHandler(db);

        await handler.HandleAsync(
            new RejectProfilePhotoRequest(companyId, employeeId, null),
            Guid.NewGuid(),
            CancellationToken.None);

        var notification = Assert.Single(notifications.Written);
        Assert.Equal(NotificationType.ProfilePhotoRejected, notification.Type);
        Assert.DoesNotContain("Reason:", notification.Body);
        Assert.Equal("Your profile photo submission has been rejected.", notification.Body);
    }

    [Fact]
    public async Task HandleAsync_Publishes_ProfilePhotoRejected_Audit_Event()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var reviewerId      = Guid.NewGuid();
        var pending           = SeedPendingPhoto(db, companyId, employeeId);
        var (handler, _, _, _, audit) = BuildHandler(db);

        await handler.HandleAsync(
            new RejectProfilePhotoRequest(companyId, employeeId, "Not clear"),
            reviewerId,
            CancellationToken.None);

        var evt = Assert.Single(audit.Published.OfType<ProfilePhotoRejectedAuditEvent>());
        Assert.Equal(companyId,        evt.CompanyId);
        Assert.Equal(pending.Id,       evt.PendingProfilePhotoId);
        Assert.Equal(employeeId,       evt.EmployeeId);
        Assert.Equal(reviewerId,       evt.ReviewedBy);
        Assert.Equal("Not clear",      evt.RejectionReason);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_No_Pending_Photo_To_Reject()
    {
        await using var db = BuildContext();
        var (handler, storage, taskCompleter, notifications, audit) = BuildHandler(db);

        var result = await handler.HandleAsync(
            new RejectProfilePhotoRequest(Guid.NewGuid(), Guid.NewGuid(), null),
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
