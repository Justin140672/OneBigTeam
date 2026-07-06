namespace HR.Modules.Employees.Features.CreateOnboardingTemplate;

internal sealed record CreateOnboardingTemplateRequest
{
    public Guid CompanyId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}
