namespace HR.Modules.Probation.Features.GetProbationReviews;

internal sealed record GetProbationReviewsResponse(IReadOnlyList<ProbationReviewItem> Items);

internal sealed record ProbationReviewItem(
    Guid Id,
    Guid ProbationRecordId,
    string ReviewType,
    DateOnly DueDate,
    string Status,
    DateTimeOffset? CompletedAt,
    Guid? CompletedByEmployeeId,
    string? Notes);
