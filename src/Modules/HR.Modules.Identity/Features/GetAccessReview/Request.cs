namespace HR.Modules.Identity.Features.GetAccessReview;

internal sealed record GetAccessReviewRequest
{
    public Guid CompanyId { get; init; }
}
