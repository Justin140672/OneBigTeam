using HR.Modules.Recruitment.Services;

namespace HR.Modules.Recruitment.Features.ApplyPositionProfileMatches;

internal sealed record ApplyPositionProfileMatchesResponse(IReadOnlyList<VacancyPositionProfileMatchResultItem> Results);

internal sealed record VacancyPositionProfileMatchResultItem(
    Guid VacancyId,
    string? AdvertTitle,
    VacancyPositionProfileMatchOutcome Outcome,
    Guid? AssignedPositionProfileId,
    IReadOnlyList<Guid> CandidatePositionProfileIds);
