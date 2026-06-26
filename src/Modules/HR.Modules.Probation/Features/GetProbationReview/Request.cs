namespace HR.Modules.Probation.Features.GetProbationReview;

internal sealed record GetProbationReviewRequest
{
    public Guid CompanyId { get; init; }
    public Guid ReviewId { get; init; }
}
