using HR.Infrastructure.Abstractions;

namespace HR.Modules.Employees.Features.UpdateOnboardingTemplate;

internal sealed record UpdateOnboardingTemplateRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public IReadOnlyList<UpdateOnboardingTemplateTaskItem> Tasks { get; init; } = [];
}

internal sealed record UpdateOnboardingTemplateTaskItem(
    Guid? Id,
    string Title,
    string? Description,
    TaskPriority Priority,
    OnboardingTemplateTaskAssignTo AssignTo,
    int DueDaysAfterStart,
    int DisplayOrder);
