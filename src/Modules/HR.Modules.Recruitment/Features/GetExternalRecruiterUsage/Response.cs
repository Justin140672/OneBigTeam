namespace HR.Modules.Recruitment.Features.GetExternalRecruiterUsage;

// Same rationale as GetRecruitmentStageUsageResponse: deactivating an ExternalRecruiter always
// remains allowed (see SetExternalRecruiterActiveStatusHandler's doc comment — the row is never
// deleted, only flagged inactive), so this is purely an advisory read used by the External
// Recruiters admin UI to decide whether to warn before deactivating a recruiter still assigned to
// at least one active (non-closed, non-cancelled) Vacancy.
internal sealed record GetExternalRecruiterUsageResponse(
    Guid ExternalRecruiterId,
    bool InUse,
    int ActiveVacancyCount,
    IReadOnlyList<string> VacancyLabels);
