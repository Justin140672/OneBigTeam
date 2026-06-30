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
    string? SerialNumber,
    string? CategoryName,
    bool IsAcknowledged)
{
    public string AcknowledgementStatus => IsAcknowledged ? "Acknowledged" : "Pending";
}

public sealed record AssetDetailModel(
    Guid Id,
    Guid CompanyId,
    string AssetNumber,
    Guid CategoryId,
    string Name,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    DateOnly? PurchaseDate,
    decimal? PurchasePrice,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? CategoryName);
