using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.CreateRecruitmentStage;

internal sealed record CreateRecruitmentStageRequest(
    Guid CompanyId,
    string Name,
    int DisplayOrder,
    bool IsTerminal,
    RecruitmentStageTerminalOutcome TerminalOutcome);
