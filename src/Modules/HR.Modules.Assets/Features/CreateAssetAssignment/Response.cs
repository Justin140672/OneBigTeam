namespace HR.Modules.Assets.Features.CreateAssetAssignment;

internal sealed record CreateAssetAssignmentResponse(
    Guid Id,
    Guid CompanyId,
    Guid AssetId,
    Guid EmployeeId,
    Guid AssignedBy,
    DateTimeOffset AssignedAt,
    string? Notes,
    DateTimeOffset CreatedAt);
