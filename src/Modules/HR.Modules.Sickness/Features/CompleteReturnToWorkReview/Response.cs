namespace HR.Modules.Sickness.Features.CompleteReturnToWorkReview;

internal sealed record CompleteReturnToWorkReviewResponse(
    Guid Id,
    Guid CompanyId,
    Guid SicknessRecordId,
    Guid EmployeeId,
    string Status,
    string Outcome,
    bool AdjustmentsRequired,
    string? AdjustmentDetails,
    Guid ReviewedBy,
    DateTimeOffset CompletedAt);
