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

// ── DASHBOARD: EXPIRING DOCUMENTS ────────────────────────────────────────────────

public sealed record GetExpiringDocumentsResponse(IReadOnlyList<ExpiringDocumentItem> Items);

public sealed record ExpiringDocumentItem(
    Guid   EmployeeDocumentId,
    Guid   EmployeeId,
    string Title,
    string DocumentTypeName,
    DateOnly ExpiryDate,
    DocumentExpiryStatus ExpiryStatus);

// --- Shared Company Documents ---

public sealed record CompanyDocumentCategoryListResponse(IReadOnlyList<CompanyDocumentCategoryItem> Items);

public sealed record CompanyDocumentCategoryItem(Guid Id, string Name, bool IsActive);

public sealed record SharedCompanyDocumentListResponse(IReadOnlyList<SharedCompanyDocumentListItem> Items);

public sealed record SharedCompanyDocumentListItem(
    Guid Id,
    string Title,
    string? Description,
    Guid CategoryId,
    string CategoryName,
    string FileName,
    int VersionNumber,
    string Status,
    DateOnly? EffectiveDate,
    DateOnly? ReviewDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string UpdatedByName);

public sealed record PublishedSharedCompanyDocumentListResponse(IReadOnlyList<PublishedSharedCompanyDocumentItem> Items);

public sealed record PublishedSharedCompanyDocumentItem(
    Guid Id,
    string Title,
    string? Description,
    string CategoryName,
    DateOnly? EffectiveDate);

public enum DocumentExpiryStatus
{
    Valid,
    ExpiringSoon,
    Expired,
}
