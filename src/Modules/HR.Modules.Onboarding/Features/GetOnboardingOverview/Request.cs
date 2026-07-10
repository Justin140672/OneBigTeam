namespace HR.Modules.Onboarding.Features.GetOnboardingOverview;

internal sealed record GetOnboardingOverviewRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
}
