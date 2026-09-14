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

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
