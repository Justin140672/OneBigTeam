namespace HR.Modules.Recruitment.Features.GetRecruitmentStageUsage;

// Used to warn (not block) an HR/recruiter user before deactivating a recruitment stage that is
// still the current stage of at least one Application belonging to an active (non-closed,
// non-cancelled) Vacancy. Deactivation itself always remains allowed (see
// SetRecruitmentStageActiveStatusHandler's doc comment) — this is purely an advisory read used by
// the settings UI to decide whether to show a confirmation prompt before calling that endpoint.
internal sealed record GetRecruitmentStageUsageResponse(
    Guid RecruitmentStageId,
    bool InUse,
    int ActiveVacancyCount,
    IReadOnlyList<string> VacancyLabels);
