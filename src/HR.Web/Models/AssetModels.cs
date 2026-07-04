using System.ComponentModel.DataAnnotations;

namespace HR.Web.Models;

// --- Asset list / CRUD models ---

public record ListAssetsAdminResponse(List<AssetListItemModel> Items);

public record AssetListItemModel(
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
    DateTimeOffset UpdatedAt);

public record CreateAssetRequest(
    Guid CompanyId,
    string AssetNumber,
    Guid CategoryId,
    string Name,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    DateOnly? PurchaseDate,
    decimal? PurchasePrice);

public record CreateAssetResponse(
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
    DateTimeOffset UpdatedAt);

public record UpdateAssetRequest(
    Guid CompanyId,
    Guid Id,
    string AssetNumber,
    Guid CategoryId,
    string Name,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    DateOnly? PurchaseDate,
    decimal? PurchasePrice);

public record UpdateAssetResponse(
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
    DateTimeOffset UpdatedAt);

public sealed class AssetEditModel
{
    [Required(ErrorMessage = "Asset number is required.")]
    public string AssetNumber { get; set; } = string.Empty;
    [Required(ErrorMessage = "Category is required.")]
    public Guid? CategoryId { get; set; }
    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public DateOnly? PurchaseDate { get; set; }
    public decimal? PurchasePrice { get; set; }
}

// --- Employee asset / assignment models ---

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

public sealed record AvailableAssetItem(
    Guid Id,
    string AssetNumber,
    string Name,
    string? Manufacturer,
    string? Model);

public sealed record AssetAssignmentItem(
    Guid Id,
    Guid AssetId,
    Guid EmployeeId,
    Guid AssignedBy,
    DateTimeOffset AssignedAt,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? ReturnedAt,
    string? Notes);

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
