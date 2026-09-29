namespace HR.Web.Models;


public sealed record GetCompanyOnboardingChecklistResponse(
    IReadOnlyList<CompanyOnboardingTaskItem> Tasks,
    int CompletionPercentage,
    bool IsHidden,
    bool IsDismissedEarly);

public sealed record CompanyOnboardingTaskItem(
    string Key,
    string Name,
    string Description,
    bool IsMandatory,
    string LinkUrl,
    int Order,
    bool IsCompleted,
    DateTimeOffset? CompletedAt);

public sealed record DismissCompanyOnboardingChecklistResponse(bool IsHidden);
