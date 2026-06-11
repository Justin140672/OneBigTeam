namespace HR.Modules.Employees.Features.GetPositionProfile;

internal sealed record GetPositionProfileRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
}
