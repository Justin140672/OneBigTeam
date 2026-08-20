namespace HR.Web.Models;

// "Getting Started" onboarding checklist shown to HR Administrators / Company Administrators
// after sign-in. Distinct from OnboardingModels.cs, which covers per-employee onboarding plans —
// this is company-level, one-time setup guidance.

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
