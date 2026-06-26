namespace HR.Modules.Probation.Features.GetUpcomingProbationReviews;

internal sealed record GetUpcomingProbationReviewsResponse(IReadOnlyList<UpcomingProbationReviewItem> Items);

internal sealed record UpcomingProbationReviewItem(
    Guid ReviewId,
    Guid ProbationRecordId,
    Guid EmployeeId,
    string ReviewType,
    DateOnly DueDate);
