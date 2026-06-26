namespace HR.Modules.Probation.Features.GetProbationReview;

internal sealed record GetProbationReviewResponse(
    Guid Id,
    Guid CompanyId,
    Guid ProbationRecordId,
    Guid EmployeeId,
    string ReviewType,
    DateOnly DueDate,
    string Status,
    DateTimeOffset? CompletedAt,
    string? Notes,
    DateOnly RecordStartDate,
    DateOnly RecordExpectedEndDate,
    string RecordStatus);
