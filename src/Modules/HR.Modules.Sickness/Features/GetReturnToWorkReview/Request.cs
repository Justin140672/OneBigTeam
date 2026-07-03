namespace HR.Modules.Sickness.Features.GetReturnToWorkReview;

internal sealed record GetReturnToWorkReviewRequest
{
    public Guid CompanyId { get; init; }
    public Guid ReviewId { get; init; }
}
