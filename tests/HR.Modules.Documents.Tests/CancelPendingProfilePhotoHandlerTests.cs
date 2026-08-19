using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.CancelPendingProfilePhoto;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class CancelPendingProfilePhotoHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 12, 10, 0, 0, DateTimeKind.Utc);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static (CancelPendingProfilePhotoHandler Handler, FakeProfilePhotoStorageService Storage, FakeAuditPublisher Audit)
        BuildHandler(DocumentsDbContext db, FakeTaskCanceller? taskCanceller = null)
    {
        var storage = new FakeProfilePhotoStorageService();
        var audit   = new FakeAuditPublisher();
        var handler = new CancelPendingProfilePhotoHandler(
            db, storage, taskCanceller ?? new FakeTaskCanceller(), new FakeClock(FixedUtcNow), audit);
        return (handler, storage, audit);
    }

    private static PendingProfilePhoto SeedPendingPhoto(
        DocumentsDbContext db, Guid companyId, Guid employeeId, string storageKey = "pending/key.png")
    {
        var pending = PendingProfilePhoto.Create(
            Guid.NewGuid(), companyId, employeeId, "avatar.png", 1234, "image/png",
            storageKey, employeeId, DateTimeOffset.UtcNow);
        db.PendingProfilePhotos.Add(pending);
        db.SaveChanges();
        return pending;
    }

    [Fact]
    public async Task HandleAsync_Deletes_Pending_Photo_And_Blob_And_Cancels_Task_And_Publishes_Audit()
    {
        await using var db      = BuildContext();
        var companyId           = Guid.NewGuid();
        var employeeId          = Guid.NewGuid();
        var taskCanceller       = new FakeTaskCanceller();
        var pending              = SeedPendingPhoto(db, companyId, employeeId);
        var (handler, storage, audit) = BuildHandler(db, taskCanceller);

        var result = await handler.HandleAsync(
            new CancelPendingProfilePhotoRequest(companyId),
            employeeId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Empty(await db.PendingProfilePhotos.ToListAsync());

        Assert.Single(storage.Deletions);
        Assert.Equal(pending.StorageKey, storage.Deletions[0]);

        var cancelCall = Assert.Single(taskCanceller.Calls);
        Assert.Equal(companyId,           cancelCall.CompanyId);
        Assert.Equal(pending.Id,          cancelCall.SourceEntityId);
        Assert.Equal(TaskSource.Document, cancelCall.Source);
        Assert.Equal(TaskActionType.Review, cancelCall.ActionType);

        var evt = Assert.Single(audit.Published.OfType<ProfilePhotoCancelledAuditEvent>());
        Assert.Equal(companyId,  evt.CompanyId);
        Assert.Equal(pending.Id, evt.PendingProfilePhotoId);
        Assert.Equal(employeeId, evt.EmployeeId);
        Assert.Equal(employeeId, evt.CancelledBy);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_No_Pending_Photo_Exists()
    {
        await using var db = BuildContext();
        var (handler, storage, audit) = BuildHandler(db);

        var result = await handler.HandleAsync(
            new CancelPendingProfilePhotoRequest(Guid.NewGuid()),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(storage.Deletions);
        Assert.Empty(audit.Published);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Touch_Other_Employees_Pending_Photos()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();

        SeedPendingPhoto(db, companyId, employeeId);
        var otherPending = SeedPendingPhoto(db, companyId, otherEmployeeId, "pending/other.png");

        var (handler, storage, _) = BuildHandler(db);

        var result = await handler.HandleAsync(
            new CancelPendingProfilePhotoRequest(companyId),
            employeeId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var remaining = await db.PendingProfilePhotos.ToListAsync();
        var remainingPhoto = Assert.Single(remaining);
        Assert.Equal(otherEmployeeId, remainingPhoto.EmployeeId);
        Assert.Equal(otherPending.Id, remainingPhoto.Id);

        Assert.DoesNotContain(otherPending.StorageKey, storage.Deletions);
    }
}
