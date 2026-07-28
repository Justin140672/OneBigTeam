namespace HR.Modules.Recruitment.Features.SetRecruitmentStageActiveStatus;

internal sealed record SetRecruitmentStageActiveStatusResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    bool IsActive,
    DateTimeOffset UpdatedAt);
