namespace HR.Modules.Probation.Features.CompleteProbationReview;

internal sealed record CompleteProbationReviewRequest
{
    public Guid CompanyId { get; init; }
    public Guid ProbationRecordId { get; init; }
    public Guid ReviewId { get; init; }
    public Guid CompletedByEmployeeId { get; init; }
    public string? Notes { get; init; }
}
