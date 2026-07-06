namespace HR.Modules.Employees.Features.CreateOnboardingTemplate;

internal sealed record CreateOnboardingTemplateResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt);
