using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.GetExternalRecruiterActivitySummary;

internal sealed record GetExternalRecruiterActivitySummaryResponse(
    Guid ExternalRecruiterId,
    string AgencyName,
    IReadOnlyList<VacancyActivityItem> CurrentVacancies,
    IReadOnlyList<VacancyActivityItem> PreviousVacancies,
    int CandidatesIntroducedCount,
    int CandidatesHiredCount);

// No fee/commission/contract/invoicing fields — explicitly out of scope for this summary.
// Ticket #81: DateInstructed (from the now-removed VacancyRecruiterAssignment row) no longer exists —
// Vacancy.OpenedAt is used instead as the closest available date signal, and is nullable because a
// vacancy that never opened (still Draft) has no OpenedAt. See GetExternalRecruiterActivitySummaryHandler
// for the "previous vacancies" redefinition this change required.
internal sealed record VacancyActivityItem(
    Guid VacancyId,
    string? AdvertTitle,
    VacancyStatus Status,
    DateOnly? DateInstructed);
