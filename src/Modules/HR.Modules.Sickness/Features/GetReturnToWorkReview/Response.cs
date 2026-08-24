namespace HR.Modules.Sickness.Features.GetReturnToWorkReview;

internal sealed record GetReturnToWorkReviewResponse(
    Guid Id,
    Guid CompanyId,
    Guid SicknessRecordId,
    Guid EmployeeId,
    DateOnly DueDate,
    string Status,
    DateTimeOffset? CompletedAt,
    string? Notes,
    string? Outcome,
    bool AdjustmentsRequired,
    string? AdjustmentDetails);
