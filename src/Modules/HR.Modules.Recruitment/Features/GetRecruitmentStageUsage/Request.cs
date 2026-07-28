namespace HR.Modules.Recruitment.Features.GetRecruitmentStageUsage;

internal sealed record GetRecruitmentStageUsageRequest(
    Guid CompanyId,
    Guid RecruitmentStageId);
