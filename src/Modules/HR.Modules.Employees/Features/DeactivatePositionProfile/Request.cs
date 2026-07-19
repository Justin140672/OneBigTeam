namespace HR.Modules.Employees.Features.DeactivatePositionProfile;

internal sealed record DeactivatePositionProfileRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
}
