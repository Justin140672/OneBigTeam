namespace HR.Modules.Recruitment.Features.GetExternalRecruiterUsage;

internal sealed record GetExternalRecruiterUsageResponse(
    Guid ExternalRecruiterId,
    bool InUse,
    int ActiveVacancyCount,
    IReadOnlyList<string> VacancyLabels);
