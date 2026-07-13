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

// --- Shared Company Document detail (HR full view) ---

public sealed record SharedCompanyDocumentDetailResponse(
    Guid Id,
    Guid CompanyId,
    string Title,
    string? Description,
    Guid CategoryId,
    string CategoryName,
    string FileName,
    long FileSize,
    string ContentType,
    int VersionNumber,
    string Status,
    DateOnly? EffectiveDate,
    DateOnly? ReviewDate,
    string AudienceDescription,
    IReadOnlyList<Guid> AudienceDepartmentIds,
    IReadOnlyList<Guid> AudienceLocationIds,
    IReadOnlyList<Guid> AudiencePositionProfileIds,
    IReadOnlyList<Guid> AudienceEmployeeIds,
    bool RequiresAcknowledgement,
    AcknowledgementProgressModel? AcknowledgementProgress,
    IReadOnlyList<SharedCompanyDocumentVersionModel> VersionHistory,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    string UpdatedByName,
    DateTimeOffset UpdatedAt);

public sealed record AcknowledgementProgressModel(
    int AcknowledgedCount,
    int EligibleCount,
    IReadOnlyList<string> AcknowledgedEmployeeNames);

public sealed record SharedCompanyDocumentVersionModel(
    int VersionNumber,
    string FileName,
    long FileSize,
    string UploadedByName,
    DateTimeOffset UploadedAt);

// --- Shared Company Document detail (employee simplified view) ---

public sealed record PublishedSharedCompanyDocumentDetailResponse(
    Guid Id,
    string Title,
    string? Description,
    string CategoryName,
    DateOnly? EffectiveDate,
    bool RequiresAcknowledgement,
    DateTimeOffset? MyAcknowledgedAt);

public sealed record AcknowledgeSharedCompanyDocumentResponseModel(
    Guid SharedCompanyDocumentId,
    int VersionNumber,
    DateTimeOffset AcknowledgedAt);

public sealed record UpdateSharedCompanyDocumentMetadataRequest(
    Guid CompanyId,
    Guid DocumentId,
    string Title,
    string? Description,
    Guid CategoryId,
    DateOnly? EffectiveDate,
    DateOnly? ReviewDate);

public sealed record UpdateSharedCompanyDocumentMetadataResponseModel(
    Guid Id,
    Guid CompanyId,
    string Title,
    string? Description,
    Guid CategoryId,
    int VersionNumber,
    string Status,
    DateOnly? EffectiveDate,
    DateOnly? ReviewDate,
    Guid UpdatedBy,
    DateTimeOffset UpdatedAt);

public sealed record UpdateSharedCompanyDocumentAudienceRequest(
    Guid CompanyId,
    Guid DocumentId,
    IReadOnlyList<Guid> AudienceDepartmentIds,
    IReadOnlyList<Guid> AudienceLocationIds,
    IReadOnlyList<Guid> AudiencePositionProfileIds,
    IReadOnlyList<Guid> AudienceEmployeeIds);

public sealed record UpdateSharedCompanyDocumentAudienceResponseModel(
    Guid Id,
    Guid CompanyId,
    IReadOnlyList<Guid> AudienceDepartmentIds,
    IReadOnlyList<Guid> AudienceLocationIds,
    IReadOnlyList<Guid> AudiencePositionProfileIds,
    IReadOnlyList<Guid> AudienceEmployeeIds,
    string AudienceDescription);

public enum DocumentExpiryStatus
{
    Valid,
    ExpiringSoon,
    Expired,
}
