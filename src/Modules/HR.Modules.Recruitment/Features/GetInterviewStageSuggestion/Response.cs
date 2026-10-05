namespace HR.Modules.Recruitment.Features.GetInterviewStageSuggestion;

internal sealed record GetInterviewStageSuggestionResponse(
    string SuggestedName,
    int DisplayOrder,
    int ActiveInterviewStageCount,
    string? StageToRenameName,
    string? RenamedStageName);
