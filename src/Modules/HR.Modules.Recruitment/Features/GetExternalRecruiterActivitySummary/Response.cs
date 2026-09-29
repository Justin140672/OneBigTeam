using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.GetExternalRecruiterActivitySummary;

internal sealed record GetExternalRecruiterActivitySummaryResponse(
    Guid ExternalRecruiterId,
    string AgencyName,
    IReadOnlyList<VacancyActivityItem> CurrentVacancies,
    IReadOnlyList<VacancyActivityItem> PreviousVacancies,
    int CandidatesIntroducedCount,
    int CandidatesHiredCount);

internal sealed record VacancyActivityItem(
    Guid VacancyId,
    string? AdvertTitle,
    VacancyStatus Status,
    DateOnly? DateInstructed);
