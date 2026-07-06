namespace HR.Modules.Employees.Features.ListOnboardingTemplates;

internal sealed record ListOnboardingTemplatesRequest
{
    public Guid CompanyId { get; init; }
    public bool IncludeInactive { get; init; } = false;
}
