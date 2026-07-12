namespace HR.Modules.Offboarding.Features.GetOffboardingStatus;

internal sealed record GetOffboardingStatusRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
}
