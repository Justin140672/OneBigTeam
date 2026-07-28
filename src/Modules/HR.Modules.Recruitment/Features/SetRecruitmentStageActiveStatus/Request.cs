namespace HR.Modules.Recruitment.Features.SetRecruitmentStageActiveStatus;

internal sealed record SetRecruitmentStageActiveStatusRequest(
    Guid CompanyId,
    Guid RecruitmentStageId,
    bool IsActive);
