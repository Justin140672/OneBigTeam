using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.UpdateRecruitmentStage;

internal sealed record UpdateRecruitmentStageResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    int DisplayOrder,
    bool IsActive,
    bool IsTerminal,
    RecruitmentStageTerminalOutcome TerminalOutcome,
    RecruitmentStagePurpose? Purpose,
    DateTimeOffset UpdatedAt);
