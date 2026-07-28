using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.ListRecruitmentStages;

internal sealed record ListRecruitmentStagesResponse(IReadOnlyList<RecruitmentStageListItem> Items);

internal sealed record RecruitmentStageListItem(
    Guid Id,
    string Name,
    int DisplayOrder,
    bool IsActive,
    bool IsTerminal,
    RecruitmentStageTerminalOutcome TerminalOutcome);
