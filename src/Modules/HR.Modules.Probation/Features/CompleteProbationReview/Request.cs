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
}
