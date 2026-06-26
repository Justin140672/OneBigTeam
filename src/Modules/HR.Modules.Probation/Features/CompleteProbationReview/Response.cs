namespace HR.Modules.Probation.Features.CompleteProbationReview;

internal sealed record CompleteProbationReviewResponse(
    Guid Id,
    Guid CompanyId,
    Guid ProbationRecordId,
    string ReviewType,
    DateOnly DueDate,
    string Status,
    DateTimeOffset? CompletedAt,
    Guid? CompletedByEmployeeId,
    string? Outcome,
    string? Notes);
