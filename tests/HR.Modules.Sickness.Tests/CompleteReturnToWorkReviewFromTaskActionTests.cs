using HR.Modules.Tasks.Contracts;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.CompleteReturnToWorkReviewFromTask;
using HR.Modules.Sickness.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

/// <summary>
/// Ticket 16 (P1): CompleteReturnToWorkReviewFromTaskAction never completes a review itself — the
/// generic Tasks-module task-completion callback has no structured fit-to-return outcome to
/// supply, and completing a review without an outcome is disallowed. A still-Pending review now
/// rejects generic completion with a validation failure (so the generic Tasks item stays open)
/// rather than silently succeeding while the review remains outstanding; the review is only ever
/// completed via the dedicated Features/CompleteReturnToWorkReview endpoint, whose own callback
/// hits the "already Completed" branch and succeeds.
/// </summary>
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
            review.Complete(Guid.NewGuid(), FitToReturnOutcome.Fit, false, null, "Already reviewed", Now);

        db.ReturnToWorkReviews.Add(review);
        await db.SaveChangesAsync();

        return review;
    }

    private static CompleteReturnToWorkReviewFromTaskAction BuildAction(SicknessDbContext db) => new(db);

    private static TaskCompletionContext BuildCompletionContext(
        Guid companyId, Guid? sourceEntityId, Guid completedBy, string? notes = null) =>
        new(companyId, Guid.NewGuid(), "Return-to-work review", null,
            TaskSource.Sickness, TaskActionType.Review,
            EmployeeId, completedBy, Now, sourceEntityId, null, notes);

    [Fact]
    public async Task ExecuteAsync_PendingReview_RejectsGenericCompletion_ReviewStaysPending()
    {
        var db = BuildDbContext();
        var review = await SeedReview(db);
        var action = BuildAction(db);
        var completedBy = Guid.NewGuid();

        var result = await action.ExecuteAsync(
            BuildCompletionContext(CompanyId, review.Id, completedBy, "Fit to return"),
            CancellationToken.None);

        // Ticket 16 (P1): a Pending review has no structured fit-to-return decision available on
        // this generic path — the action now rejects completion (so CompleteTaskHandler leaves the
        // generic Tasks item open/actionable, per ITaskCompletionAction's "a failed Result aborts
        // completion entirely" contract) instead of silently succeeding while the review itself
        // stays outstanding.
        Assert.True(result.IsFailure);

        var updated = await db.ReturnToWorkReviews.FindAsync(review.Id);
        Assert.Equal(ReturnToWorkReviewStatus.Pending, updated!.Status);
        Assert.Null(updated.ReviewedBy);
        Assert.Null(updated.Notes);
        Assert.Null(updated.CompletedAt);
        Assert.Null(updated.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAlreadyCompleted_IsNoOp_DoesNotChangeReview()
    {
        var db = BuildDbContext();
        var review = await SeedReview(db, completed: true);
        var originalReviewedBy = review.ReviewedBy;
        var originalNotes = review.Notes;
        var action = BuildAction(db);

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
}
