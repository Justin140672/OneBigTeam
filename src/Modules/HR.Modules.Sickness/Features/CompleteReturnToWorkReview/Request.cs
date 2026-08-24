using HR.Modules.Sickness.Domain;

namespace HR.Modules.Sickness.Features.CompleteReturnToWorkReview;

internal sealed record CompleteReturnToWorkReviewRequest
{
    public Guid CompanyId { get; init; }
    public Guid ReviewId { get; init; }
    public FitToReturnOutcome Outcome { get; init; }
    public bool AdjustmentsRequired { get; init; }
    public string? AdjustmentDetails { get; init; }
    public string? ManagerNotes { get; init; }
}
