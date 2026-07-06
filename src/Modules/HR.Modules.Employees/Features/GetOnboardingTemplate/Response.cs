using HR.Infrastructure.Abstractions;

namespace HR.Modules.Employees.Features.GetOnboardingTemplate;

internal sealed record GetOnboardingTemplateResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<OnboardingTemplateTaskListItem> Tasks);

internal sealed record OnboardingTemplateTaskListItem(
    Guid Id,
    string Title,
    string? Description,
    TaskPriority Priority,
    OnboardingTemplateTaskAssignTo AssignTo,
    int DueDaysAfterStart,
    int DisplayOrder);
