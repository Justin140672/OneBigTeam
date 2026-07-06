namespace HR.Modules.Employees.Features.ListRequiredAssetsForPositionProfile;

internal sealed record ListRequiredAssetsRequest
{
    public Guid CompanyId { get; init; }
    public Guid PositionProfileId { get; init; }
}
