using HR.Modules.Tasks.Contracts;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.CompleteReturnToWorkReviewFromTask;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Tests.Infrastructure;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;
using HR.Modules.Sickness;

namespace HR.Modules.Sickness.Tests;

public class CompleteReturnToWorkReviewFromTaskActionTests
{
    private static readonly DateTime NowUtc = new(2026, 7, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(NowUtc, TimeSpan.Zero);
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private static SicknessDbContext BuildDbContext() =>
        new(new DbContextOptionsBuilder<SicknessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static async Task<ReturnToWorkReview> SeedReview(
        SicknessDbContext db,
        bool completed = false)
    {
        var review = ReturnToWorkReview.Create(
            Guid.NewGuid(), CompanyId, Guid.NewGuid(), EmployeeId,
            new DateOnly(2026, 7, 9), Now);

        if (completed)
            review.Complete(Guid.NewGuid(), "Already reviewed", Now);

        db.ReturnToWorkReviews.Add(review);
        await db.SaveChangesAsync();

        return review;
    }

    private static CompleteReturnToWorkReviewFromTaskAction BuildAction(
        SicknessDbContext db,
        DateTime? nowUtc = null,
        FakeAuditEventPublisher? auditPublisher = null) =>
        new(db, new FakeClock(nowUtc ?? NowUtc), auditPublisher ?? new FakeAuditEventPublisher());

    private static TaskCompletionContext BuildCompletionContext(
        Guid companyId, Guid? sourceEntityId, Guid completedBy, string? notes = null) =>
        new(companyId, Guid.NewGuid(), "Return-to-work review", null,
            TaskSource.Sickness, TaskActionType.Review,
            EmployeeId, completedBy, Now, sourceEntityId, null, notes);

    [Fact]
    public async Task ExecuteAsync_CompletesPendingReview_SetsNotesReviewedByAndCompletedAt()
    {
        var db = BuildDbContext();
        var review = await SeedReview(db);
        var action = BuildAction(db);
        var completedBy = Guid.NewGuid();

        await action.ExecuteAsync(
            BuildCompletionContext(CompanyId, review.Id, completedBy, "Fit to return"),
            CancellationToken.None);

        var updated = await db.ReturnToWorkReviews.FindAsync(review.Id);
        Assert.Equal(ReturnToWorkReviewStatus.Completed, updated!.Status);
        Assert.Equal(completedBy, updated.ReviewedBy);
        Assert.Equal("Fit to return", updated.Notes);
        Assert.Equal(Now, updated.CompletedAt);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAlreadyCompleted_DoesNotChangeReview()
    {
        var db = BuildDbContext();
        var review = await SeedReview(db, completed: true);
        var originalReviewedBy = review.ReviewedBy;
        var originalNotes = review.Notes;
        var action = BuildAction(db, NowUtc.AddHours(1));

        await action.ExecuteAsync(
            BuildCompletionContext(CompanyId, review.Id, Guid.NewGuid(), "New notes"),
            CancellationToken.None);

        var updated = await db.ReturnToWorkReviews.FindAsync(review.Id);
        Assert.Equal(ReturnToWorkReviewStatus.Completed, updated!.Status);
        Assert.Equal(originalReviewedBy, updated.ReviewedBy);
        Assert.Equal(originalNotes, updated.Notes);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSourceEntityIdDoesNotMatchAnyReview_DoesNothing()
    {
        var db = BuildDbContext();
        var review = await SeedReview(db);
        var action = BuildAction(db);

        await action.ExecuteAsync(
            BuildCompletionContext(CompanyId, Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        var updated = await db.ReturnToWorkReviews.FindAsync(review.Id);
        Assert.Equal(ReturnToWorkReviewStatus.Pending, updated!.Status);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSourceEntityIdIsNull_DoesNothing()
    {
        var db = BuildDbContext();
        var review = await SeedReview(db);
        var action = BuildAction(db);

        await action.ExecuteAsync(
            BuildCompletionContext(CompanyId, sourceEntityId: null, Guid.NewGuid()),
            CancellationToken.None);

        var updated = await db.ReturnToWorkReviews.FindAsync(review.Id);
        Assert.Equal(ReturnToWorkReviewStatus.Pending, updated!.Status);
    }

    [Fact]
    public void Source_ReturnsSickness() =>
        Assert.Equal(TaskSource.Sickness, BuildAction(BuildDbContext()).Source);

    [Fact]
    public void ActionType_ReturnsReview() =>
        Assert.Equal(TaskActionType.Review, BuildAction(BuildDbContext()).ActionType);

    [Fact]
    public async Task ExecuteAsync_PublishesAuditEvent_OnSuccessfulCompletion()
    {
        var db = BuildDbContext();
        var review = await SeedReview(db);
        var auditPublisher = new FakeAuditEventPublisher();
        var action = BuildAction(db, auditPublisher: auditPublisher);
        var completedBy = Guid.NewGuid();

        await action.ExecuteAsync(
            BuildCompletionContext(CompanyId, review.Id, completedBy, "Fit to return"),
            CancellationToken.None);

        var auditEvents = auditPublisher.PublishedEvents.OfType<ReturnToWorkReviewCompletedAuditEvent>().ToList();
        Assert.Single(auditEvents);
        var evt = auditEvents[0];
        Assert.Equal(review.Id, evt.ReviewId);
        Assert.Equal(review.SicknessRecordId, evt.SicknessRecordId);
        Assert.Equal(CompanyId, evt.CompanyId);
        Assert.Equal(EmployeeId, evt.EmployeeId);
        Assert.Equal(completedBy, evt.ReviewedBy);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotPublishAuditEvent_WhenAlreadyCompleted()
    {
        var db = BuildDbContext();
        var review = await SeedReview(db, completed: true);
        var auditPublisher = new FakeAuditEventPublisher();
        var action = BuildAction(db, auditPublisher: auditPublisher);

        await action.ExecuteAsync(
            BuildCompletionContext(CompanyId, review.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Empty(auditPublisher.PublishedEvents.OfType<ReturnToWorkReviewCompletedAuditEvent>());
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotPublishAuditEvent_WhenSourceEntityIdIsNull()
    {
        var db = BuildDbContext();
        await SeedReview(db);
        var auditPublisher = new FakeAuditEventPublisher();
        var action = BuildAction(db, auditPublisher: auditPublisher);

        await action.ExecuteAsync(
            BuildCompletionContext(CompanyId, sourceEntityId: null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Empty(auditPublisher.PublishedEvents.OfType<ReturnToWorkReviewCompletedAuditEvent>());
    }
}
