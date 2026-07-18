namespace HR.Modules.Recruitment.Features.GetVacanciesNeedingPositionProfileReview;

internal sealed record GetVacanciesNeedingPositionProfileReviewRequest
{
    public Guid CompanyId { get; init; }
}
