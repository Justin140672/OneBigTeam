namespace HR.Modules.Onboarding.Features.GetMyOnboardingStatus;

internal sealed record GetMyOnboardingStatusResponse(
    bool HasPlan,
    string? PlanStatus,
    DateOnly? StartDate,
    int TotalTasks,
    int CompletedTasks,
    IReadOnlyList<MyOnboardingTaskItem> Tasks);

internal sealed record MyOnboardingTaskItem(
    Guid Id,
    string Title,
    string Status,
    DateOnly? DueDate,
    DateTimeOffset? CompletedAt);
