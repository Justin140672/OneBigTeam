using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.CreateRecruitmentStage;

internal sealed record CreateRecruitmentStageResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    int DisplayOrder,
    bool IsActive,
    bool IsTerminal,
    RecruitmentStageTerminalOutcome TerminalOutcome,
    RecruitmentStagePurpose? Purpose,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
