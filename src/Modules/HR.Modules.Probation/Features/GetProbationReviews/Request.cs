namespace HR.Modules.Probation.Features.GetProbationReviews;

internal sealed record GetProbationReviewsRequest
{
    public Guid CompanyId { get; init; }
    public Guid ProbationRecordId { get; init; }
}
