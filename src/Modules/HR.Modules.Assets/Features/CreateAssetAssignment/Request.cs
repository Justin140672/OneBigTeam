namespace HR.Modules.Assets.Features.CreateAssetAssignment;

internal sealed record CreateAssetAssignmentRequest
{
    public Guid CompanyId { get; init; }
    public Guid AssetId { get; init; }
    public Guid EmployeeId { get; init; }
    public Guid AssignedBy { get; init; }
    public string? Notes { get; init; }
}
