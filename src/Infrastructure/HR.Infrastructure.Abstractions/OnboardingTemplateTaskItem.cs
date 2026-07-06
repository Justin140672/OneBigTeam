namespace HR.Infrastructure.Abstractions;

public sealed record OnboardingTemplateTaskItem(
    Guid Id,
    string Title,
    string? Description,
    TaskPriority Priority,
    OnboardingTemplateTaskAssignTo AssignTo,
    int DueDaysAfterStart,
    int DisplayOrder);
