namespace HR.Modules.Assets.Features.ListAssetAssignments;

internal sealed record ListAssetAssignmentsResponse(
    Guid Id,
    Guid CompanyId,
    Guid AssetId,
    Guid EmployeeId,
    Guid AssignedBy,
    DateTimeOffset AssignedAt,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? ReturnedAt,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsActive);
