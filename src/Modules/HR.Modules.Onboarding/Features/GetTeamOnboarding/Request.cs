namespace HR.Modules.Onboarding.Features.GetTeamOnboarding;

internal sealed record GetTeamOnboardingRequest
{
    public Guid CompanyId { get; init; }
    public Guid ManagerId { get; init; }
}
