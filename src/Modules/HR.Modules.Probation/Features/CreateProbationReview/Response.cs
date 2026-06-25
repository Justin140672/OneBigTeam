namespace HR.Modules.Probation.Features.CreateProbationReview;

internal sealed record CreateProbationReviewResponse(
    Guid Id,
    Guid CompanyId,
    Guid ProbationRecordId,
    string ReviewType,
    DateOnly DueDate,
    string Status,
    DateTimeOffset CreatedAt);
