namespace HR.Modules.Assets.Features.ListEmployeeAssets;

internal sealed record ListEmployeeAssetsRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
}
