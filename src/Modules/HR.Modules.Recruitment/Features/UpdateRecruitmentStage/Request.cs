using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.UpdateRecruitmentStage;

internal sealed record UpdateRecruitmentStageRequest(
    Guid CompanyId,
    Guid RecruitmentStageId,
    string Name,
    bool IsTerminal,
    RecruitmentStageTerminalOutcome TerminalOutcome,
    RecruitmentStagePurpose? Purpose = null);
