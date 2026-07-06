namespace HR.Modules.Employees.Features.RemoveRequiredAssetFromPositionProfile;

internal sealed record RemoveRequiredAssetRequest
{
    public Guid CompanyId { get; init; }
    public Guid PositionProfileId { get; init; }
    public Guid Id { get; init; }
}
