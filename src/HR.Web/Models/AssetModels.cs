namespace HR.Web.Models;

public sealed record EmployeeAssetItem(
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
    string? SerialNumber);
