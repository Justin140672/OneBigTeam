namespace HR.Modules.CompanyOnboarding.Features.GetOnboardingChecklist;

internal sealed record OnboardingTaskItemResponse(
    string Key,
    string Name,
    string Description,
    bool IsMandatory,
    string LinkUrl,
    int Order,
    bool IsCompleted,
    DateTimeOffset? CompletedAt);

internal sealed record GetOnboardingChecklistResponse(
    IReadOnlyList<OnboardingTaskItemResponse> Tasks,
    int CompletionPercentage,
    bool IsHidden,
    bool IsDismissedEarly);
