using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.CompleteProfilePhotoReviewFromTask;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

// Ticket: previously NO ITaskCompletionAction was registered for (Source: Document, ActionType:
// Review) — a direct call to the generic "Complete task" endpoint would mark a profile-photo
// review task Completed with zero business effect (no approval/rejection, pending submission left
// dangling forever). CompleteProfilePhotoReviewFromTaskAction closes that gap by requiring an
// explicit Approve/Reject decision and dispatching to the SAME Approve/RejectProfilePhotoHandler
// used by the dedicated endpoints.
public class CompleteProfilePhotoReviewFromTaskActionTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 12, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static DocumentsDbContext BuildDbContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static CompleteProfilePhotoReviewFromTaskAction BuildAction(DocumentsDbContext db) =>
        BuildActionWithFakes(db).Action;

    private static (CompleteProfilePhotoReviewFromTaskAction Action, FakeAuditPublisher Audit) BuildActionWithFakes(
        DocumentsDbContext db)
    {
        var storage       = new FakeProfilePhotoStorageService();
        var notifications = new FakeNotificationWriter();
        var audit         = new FakeAuditPublisher();
        var clock         = new FakeClock(FixedUtcNow);

        var reviewer = new ProfilePhotoReviewer(db, storage, clock, audit, notifications);

        return (new CompleteProfilePhotoReviewFromTaskAction(db, reviewer), audit);
    }

    private static PendingProfilePhoto SeedPendingPhoto(
        DocumentsDbContext db, Guid companyId, Guid employeeId, string storageKey = "pending/key.png",
        string fileName = "pending.png")
    {
        var pending = PendingProfilePhoto.Create(
            Guid.NewGuid(), companyId, employeeId, fileName, 222, "image/png",
            storageKey, employeeId, DateTimeOffset.UtcNow);
        db.PendingProfilePhotos.Add(pending);
        db.SaveChanges();
        return pending;
    }

    private static TaskCompletionContext BuildCompletionContext(
        Guid companyId,
        Guid? sourceEntityId,
        string? outcomeDecision,
        string? outcomeReason = null,
        Guid dispatchOperationId = default,
        Guid? completedBy = null) =>
        new(
            companyId, Guid.NewGuid(), "Review profile photo", null,
            TaskSource.Document, TaskActionType.Review,
            AssignedEmployeeId: null,
            CompletedBy: completedBy ?? Guid.NewGuid(),
            CompletedAt: Now,
            SourceEntityId: sourceEntityId,
            OutcomeDecision: outcomeDecision,
            OutcomeReason: outcomeReason,
            DispatchOperationId: dispatchOperationId);

    [Fact]
    public void Source_ReturnsDocument()
    {
        var db = BuildDbContext();
        Assert.Equal(TaskSource.Document, BuildAction(db).Source);
    }

    [Fact]
    public void ActionType_ReturnsReview()
    {
        var db = BuildDbContext();
        Assert.Equal(TaskActionType.Review, BuildAction(db).ActionType);
    }

    [Fact]
    public async Task ExecuteAsync_Fails_When_Task_Has_No_SourceEntityId()
    {
        var db = BuildDbContext();
        var action = BuildAction(db);

        var result = await action.ExecuteAsync(
            BuildCompletionContext(Guid.NewGuid(), sourceEntityId: null, outcomeDecision: "Approve"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task ExecuteAsync_Fails_When_OutcomeDecision_Is_Null()
    {
        var db = BuildDbContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var pending    = SeedPendingPhoto(db, companyId, employeeId);
        var action     = BuildAction(db);

        var result = await action.ExecuteAsync(
            BuildCompletionContext(companyId, pending.Id, outcomeDecision: null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);

        // Neither handler ran — the pending submission is untouched.
        Assert.Single(await db.PendingProfilePhotos.ToListAsync());
        Assert.Empty(await db.EmployeeProfilePhotos.ToListAsync());
    }

    [Fact]
    public async Task ExecuteAsync_Fails_When_OutcomeDecision_Is_Unrecognized()
    {
        var db = BuildDbContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var pending    = SeedPendingPhoto(db, companyId, employeeId);
        var action     = BuildAction(db);

        var result = await action.ExecuteAsync(
            BuildCompletionContext(companyId, pending.Id, outcomeDecision: "Maybe"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);

        Assert.Single(await db.PendingProfilePhotos.ToListAsync());
        Assert.Empty(await db.EmployeeProfilePhotos.ToListAsync());
    }

    [Fact]
    public async Task ExecuteAsync_Approve_With_Valid_Pending_Photo_Promotes_It_To_Live_Photo()
    {
        var db = BuildDbContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var pending    = SeedPendingPhoto(db, companyId, employeeId, "pending/key.png", "avatar.png");
        var action     = BuildAction(db);

        var result = await action.ExecuteAsync(
            BuildCompletionContext(companyId, pending.Id, outcomeDecision: "Approve"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var live = await db.EmployeeProfilePhotos.SingleAsync();
        Assert.Equal(employeeId,         live.EmployeeId);
        Assert.Equal("avatar.png",       live.FileName);
        Assert.Equal(pending.StorageKey, live.StorageKey);

        Assert.Empty(await db.PendingProfilePhotos.ToListAsync());
    }

    [Fact]
    public async Task ExecuteAsync_Reject_With_Valid_Pending_Photo_Rejects_It_And_Passes_Through_OutcomeReason()
    {
        var db = BuildDbContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var pending    = SeedPendingPhoto(db, companyId, employeeId);
        var action     = BuildAction(db);

        var result = await action.ExecuteAsync(
            BuildCompletionContext(companyId, pending.Id, outcomeDecision: "Reject", outcomeReason: "Blurry photo"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(await db.PendingProfilePhotos.ToListAsync());
        Assert.Empty(await db.EmployeeProfilePhotos.ToListAsync());
    }

    [Fact]
    public async Task ExecuteAsync_Reject_Publishes_Audit_Event_With_OutcomeReason_As_RejectionReason()
    {
        var db = BuildDbContext();
        var companyId  = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var pending    = SeedPendingPhoto(db, companyId, employeeId);

        var (action, audit) = BuildActionWithFakes(db);

        await action.ExecuteAsync(
            BuildCompletionContext(companyId, pending.Id, outcomeDecision: "Reject", outcomeReason: "Not clear"),
            CancellationToken.None);

        var evt = Assert.Single(audit.Published.OfType<ProfilePhotoRejectedAuditEvent>());
        Assert.Equal("Not clear", evt.RejectionReason);
    }

    [Fact]
    public async Task ExecuteAsync_Missing_Pending_Photo_With_Empty_DispatchOperationId_Returns_NotFound()
    {
        var db = BuildDbContext();
        var companyId = Guid.NewGuid();
        var action    = BuildAction(db);

        var result = await action.ExecuteAsync(
            BuildCompletionContext(companyId, Guid.NewGuid(), outcomeDecision: "Approve", dispatchOperationId: Guid.Empty),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task ExecuteAsync_Missing_Pending_Photo_With_NonEmpty_DispatchOperationId_Returns_Success_ReplaySafe()
    {
        // Ticket 15 (P1) replay-safety: a resumed dispatch for the SAME operation can legitimately
        // find the submission already gone because the first attempt's write already committed —
        // must be treated as already-resolved rather than failing (which would permanently stick
        // the task, since the submission can never reappear).
        var db = BuildDbContext();
        var companyId = Guid.NewGuid();
        var action    = BuildAction(db);

        var result = await action.ExecuteAsync(
            BuildCompletionContext(
                companyId, Guid.NewGuid(), outcomeDecision: "Approve", dispatchOperationId: Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
