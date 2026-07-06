namespace HR.Modules.Employees.Features.GetOnboardingTemplate;

internal sealed record GetOnboardingTemplateRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
}
