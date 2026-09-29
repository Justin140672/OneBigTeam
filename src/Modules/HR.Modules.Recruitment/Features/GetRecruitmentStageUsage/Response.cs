namespace HR.Modules.Recruitment.Features.GetRecruitmentStageUsage;

internal sealed record GetRecruitmentStageUsageResponse(
    Guid RecruitmentStageId,
    bool InUse,
    int ActiveVacancyCount,
    IReadOnlyList<string> VacancyLabels);
