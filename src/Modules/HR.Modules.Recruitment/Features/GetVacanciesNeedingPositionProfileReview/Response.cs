using HR.Modules.Recruitment.Services;

namespace HR.Modules.Recruitment.Features.GetVacanciesNeedingPositionProfileReview;

internal sealed record GetVacanciesNeedingPositionProfileReviewResponse(IReadOnlyList<VacancyPositionProfileReviewItem> Items);

internal sealed record VacancyPositionProfileReviewItem(
    Guid VacancyId,
    string? AdvertTitle,
    VacancyPositionProfileMatchOutcome Outcome,
    IReadOnlyList<Guid> CandidatePositionProfileIds);
