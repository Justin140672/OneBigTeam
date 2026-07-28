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
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
