namespace HR.Modules.Assets.Features.GetAssetAssignment;

internal sealed record GetAssetAssignmentResponse(
    Guid Id,
    Guid CompanyId,
    Guid AssetId,
    Guid EmployeeId,
    Guid AssignedBy,
    DateTimeOffset AssignedAt,
    DateTimeOffset? ReturnedAt,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsActive);
