namespace HR.Modules.Employees.Features.SetDefaultOnboardingTemplate;

internal sealed record SetDefaultOnboardingTemplateRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }

    internal string? IdempotencyKey { get; init; }
}
