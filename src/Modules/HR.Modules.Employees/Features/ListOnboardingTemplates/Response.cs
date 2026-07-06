namespace HR.Modules.Employees.Features.ListOnboardingTemplates;

internal sealed record ListOnboardingTemplatesResponse(IReadOnlyList<OnboardingTemplateListItem> Items);

internal sealed record OnboardingTemplateListItem(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    int TaskCount);
