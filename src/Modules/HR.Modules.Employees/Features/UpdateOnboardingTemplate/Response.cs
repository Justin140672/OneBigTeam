using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Employees.Features.UpdateOnboardingTemplate;

internal sealed record UpdateOnboardingTemplateResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<UpdateOnboardingTemplateTaskResult> Tasks);

internal sealed record UpdateOnboardingTemplateTaskResult(
    Guid Id,
    string Title,
    string? Description,
    TaskPriority Priority,
    OnboardingTemplateTaskAssignTo AssignTo,
    int DueDaysAfterStart,
    int DisplayOrder);
