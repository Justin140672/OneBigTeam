using System.ComponentModel.DataAnnotations;

namespace HR.Web.Models;

public sealed record EmployeeDocumentListResponse(IReadOnlyList<EmployeeDocumentListItem> Items);

public sealed record EmployeeDocumentListItem(
    Guid     EmployeeDocumentId,
    string   Title,
    string   DocumentTypeName,
    string   Status,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate,
    bool     IsAcknowledged,
    DateTimeOffset CreatedAt);

public sealed record EmployeeDocumentDetailResponse(string? DownloadUrl);

public sealed record DocumentTypeListResponse(IReadOnlyList<DocumentTypeItem> Items);

public sealed record DocumentTypeItem(Guid Id, string Name, string? Description, bool IsActive, bool AllowEmployeeUpload);

// --- Document Type CRUD models ---

public record ListDocumentTypesAdminResponse(List<DocumentTypeListItemModel> Items);

public record DocumentTypeListItemModel(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    bool AllowEmployeeUpload);

public record CreateDocumentTypeRequest(Guid CompanyId, string Name, string? Description, bool AllowEmployeeUpload);

public record CreateDocumentTypeResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    bool AllowEmployeeUpload,
    DateTimeOffset CreatedAt);

public record UpdateDocumentTypeRequest(Guid CompanyId, Guid DocumentTypeId, string Name, string? Description, bool AllowEmployeeUpload);

public record UpdateDocumentTypeResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    bool IsActive,
    bool AllowEmployeeUpload,
    DateTimeOffset UpdatedAt);

public sealed class DocumentTypeEditModel
{
    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool AllowEmployeeUpload { get; set; }
}

public sealed record DocumentRequestListResponse(IReadOnlyList<DocumentRequestListItem> Items);

public sealed record DocumentRequestListItem(
    Guid      Id,
    string    DocumentTypeName,
    DateOnly? DueDate,
    string    Status,
    Guid?     RequestedByEmployeeId,
    string?   RequestedByName,
    DateTimeOffset CreatedAt);

public sealed record GetDocumentRequestResponse(
    Guid      Id,
    string    DocumentTypeName,
    DateOnly? DueDate,
    string    Status,
    Guid?     RequestedByEmployeeId,
    string?   RequestedByName,
    DateTimeOffset CreatedAt);
