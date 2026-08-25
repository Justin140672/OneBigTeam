namespace HR.Modules.Probation.Features.CreateProbationReview;

internal sealed record CreateProbationReviewRequest
{
    public Guid CompanyId { get; init; }
    public Guid ProbationRecordId { get; init; }
    public string ReviewType { get; init; } = string.Empty;
    public DateOnly DueDate { get; init; }

    // PROB-07: populated by the endpoint from the authenticated user's resolved identity — never
    // bound from the client body.
    internal Guid? ActorEmployeeId { get; init; }
}
