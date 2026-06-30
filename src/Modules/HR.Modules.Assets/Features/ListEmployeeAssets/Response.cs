namespace HR.Modules.Assets.Features.ListEmployeeAssets;

internal sealed record ListEmployeeAssetsResponse(
    Guid Id,
    Guid AssetId,
    Guid EmployeeId,
    Guid AssignedBy,
    DateTimeOffset AssignedAt,
    string? Notes,
    string AssetNumber,
    string Name,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? CategoryName,
    bool IsAcknowledged);
