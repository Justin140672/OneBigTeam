namespace HR.Modules.Onboarding.Features.GetOnboardingStatus;

internal sealed record GetOnboardingStatusRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
}
