namespace HR.Modules.Assets.Features.GetAssetAssignment;

internal sealed class GetAssetAssignmentRequest
{
    public Guid CompanyId { get; set; }
    public Guid AssetId { get; set; }
    public Guid Id { get; set; }
}
