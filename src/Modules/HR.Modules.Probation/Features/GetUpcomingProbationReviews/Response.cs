namespace HR.Modules.Probation.Features.GetUpcomingProbationReviews;

internal sealed record GetUpcomingProbationReviewsResponse(IReadOnlyList<UpcomingProbationReviewItem> Items);

internal sealed record UpcomingProbationReviewItem(
    Guid ReviewId,
    Guid ProbationRecordId,
    Guid EmployeeId,
    string ReviewType,
    DateOnly DueDate,
    // Null when there's no open (Open/InProgress) review task for this review — e.g. it
    // predates task creation, or the task has already been completed/cancelled.
    Guid? TaskId = null);
