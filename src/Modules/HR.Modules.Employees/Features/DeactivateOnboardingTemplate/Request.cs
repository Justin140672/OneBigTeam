namespace HR.Modules.Employees.Features.DeactivateOnboardingTemplate;

internal sealed record DeactivateOnboardingTemplateRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
}
