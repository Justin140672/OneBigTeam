using HR.Modules.Probation.Domain;

namespace HR.Modules.Probation.Features.CompleteProbationReview;

internal sealed record CompleteProbationReviewRequest
{
    public Guid CompanyId { get; init; }
    public Guid ProbationRecordId { get; init; }
    public Guid ReviewId { get; init; }
    public string? Notes { get; init; }
    public ProbationOutcome? Outcome { get; init; }
    public DateOnly? DecisionDate { get; init; }
    public DateOnly? NewExpectedEndDate { get; init; }
    public string? ExtensionReason { get; init; }

    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
