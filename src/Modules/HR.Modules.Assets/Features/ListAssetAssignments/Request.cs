namespace HR.Modules.Assets.Features.ListAssetAssignments;

internal sealed class ListAssetAssignmentsRequest
{
    public Guid CompanyId { get; set; }
    public Guid AssetId { get; set; }
}
