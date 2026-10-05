namespace HR.Modules.Recruitment.Features.AddInterviewStage;

internal sealed record AddInterviewStageResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    int DisplayOrder,
    bool IsActive,
    Guid? RenamedStageId,
    string? RenamedStageName);
