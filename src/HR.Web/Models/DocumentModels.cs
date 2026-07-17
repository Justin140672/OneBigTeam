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

// ── DASHBOARD: DOCUMENT REVIEWS DUE ──────────────────────────────────────────────

public sealed record GetSharedCompanyDocumentsDueForReviewResponse(IReadOnlyList<SharedCompanyDocumentDueForReviewItem> Items);

public sealed record SharedCompanyDocumentDueForReviewItem(
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
    string ReviewFrequency,
    Guid? ReviewOwnerEmployeeId,
    string? ReviewOwnerName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string UpdatedByName,
    bool IsOverdue);

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
    string ReviewFrequency,
    Guid? ReviewOwnerEmployeeId,
    string? ReviewOwnerName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string UpdatedByName);

public static class ReviewFrequencyDisplay
{
    public static string Label(string value) => value == "SixMonthly" ? "Six Monthly" : value;
}

public sealed record PublishedSharedCompanyDocumentListResponse(IReadOnlyList<PublishedSharedCompanyDocumentItem> Items);

public sealed record PublishedSharedCompanyDocumentItem(
    Guid Id,
    string Title,
    string? Description,
    string CategoryName,
    DateOnly? EffectiveDate,
    bool RequiresAcknowledgement,
    DateOnly? AcknowledgementDueDate,
    DateTimeOffset? MyAcknowledgedAt,
    DateTimeOffset? PublishedAt);

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
    string ReviewFrequency,
    int? CustomReviewFrequencyMonths,
    Guid? ReviewOwnerEmployeeId,
    string? ReviewOwnerName,
    string AudienceDescription,
    IReadOnlyList<Guid> AudienceDepartmentIds,
    IReadOnlyList<Guid> AudienceLocationIds,
    IReadOnlyList<Guid> AudiencePositionProfileIds,
    IReadOnlyList<Guid> AudienceEmployeeIds,
    bool RequiresAcknowledgement,
    DateOnly? AcknowledgementDueDate,
    string? AcknowledgementStatement,
    AcknowledgementProgressModel? AcknowledgementProgress,
    IReadOnlyList<SharedCompanyDocumentVersionModel> VersionHistory,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    string UpdatedByName,
    DateTimeOffset UpdatedAt,
    string? PublishedByName,
    DateTimeOffset? PublishedAt,
    string? ArchivedByName,
    DateTimeOffset? ArchivedAt,
    string? ArchiveReason,
    DateOnly? LastReviewedAt,
    Guid? LastReviewedByEmployeeId,
    string? LastReviewedByName,
    string? LastReviewNotes,
    IReadOnlyList<SharedCompanyDocumentReviewHistoryModel> ReviewHistory);

public sealed record AcknowledgementProgressModel(
    int AcknowledgedCount,
    int EligibleCount,
    IReadOnlyList<string> AcknowledgedEmployeeNames);

public sealed record SharedCompanyDocumentVersionModel(
    int VersionNumber,
    string FileName,
    long FileSize,
    string UploadedByName,
    DateTimeOffset UploadedAt,
    string? VersionNote,
    bool RequiresAcknowledgement,
    DateOnly? EffectiveDate,
    string PublicationStatus);

public sealed record SharedCompanyDocumentReviewHistoryModel(
    DateOnly ReviewDate,
    Guid ReviewedByEmployeeId,
    string ReviewedByName,
    string? ReviewNotes,
    DateOnly? PreviousReviewDate);

// --- Shared Company Document acknowledgement progress (HR full view) ---

public sealed record SharedCompanyDocumentAcknowledgementProgressResponse(
    Guid DocumentId,
    string DocumentTitle,
    int TotalAssigned,
    int AcknowledgedCount,
    int OutstandingCount,
    int OverdueCount,
    decimal AcknowledgementPercentage,
    IReadOnlyList<SharedCompanyDocumentAcknowledgementProgressItem> Items);

public sealed record SharedCompanyDocumentAcknowledgementProgressItem(
    Guid EmployeeId,
    string EmployeeName,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? LocationId,
    string? LocationName,
    string Status,
    DateOnly? DueDate,
    DateTimeOffset? AcknowledgedAt);

// --- Shared Company Document detail (employee simplified view) ---

public sealed record PublishedSharedCompanyDocumentDetailResponse(
    Guid Id,
    string Title,
    string? Description,
    string CategoryName,
    DateOnly? EffectiveDate,
    bool RequiresAcknowledgement,
    DateOnly? AcknowledgementDueDate,
    string? AcknowledgementStatement,
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
    DateOnly? ReviewDate,
    string ReviewFrequency,
    int? CustomReviewFrequencyMonths,
    Guid? ReviewOwnerEmployeeId);

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
    string ReviewFrequency,
    int? CustomReviewFrequencyMonths,
    Guid? ReviewOwnerEmployeeId,
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

public sealed record PublishSharedCompanyDocumentResponseModel(
    Guid Id,
    Guid CompanyId,
    string Title,
    string Status,
    Guid PublishedBy,
    DateTimeOffset PublishedAt,
    int AcknowledgementTasksCreated);

public sealed record UpdateSharedCompanyDocumentAcknowledgementSettingsRequest(
    Guid CompanyId,
    Guid DocumentId,
    bool RequiresAcknowledgement,
    DateOnly? AcknowledgementDueDate,
    string? AcknowledgementStatement);

public sealed record ArchiveSharedCompanyDocumentRequest(
    Guid CompanyId,
    Guid DocumentId,
    string Reason);

public sealed record ExpireSharedCompanyDocumentResponseModel(
    Guid Id,
    Guid CompanyId,
    string Status,
    Guid ExpiredBy,
    DateTimeOffset ExpiredAt,
    int ReviewTasksCancelled);

public sealed record CompleteSharedCompanyDocumentReviewRequest(
    Guid CompanyId,
    Guid DocumentId,
    string ReviewNotes);

public sealed record CompleteSharedCompanyDocumentReviewResponseModel(
    Guid Id,
    Guid CompanyId,
    DateOnly? ReviewDate,
    DateOnly LastReviewedAt,
    Guid LastReviewedByEmployeeId,
    string? LastReviewNotes);

public sealed record UpdateSharedCompanyDocumentAcknowledgementSettingsResponseModel(
    Guid Id,
    Guid CompanyId,
    bool RequiresAcknowledgement,
    DateOnly? AcknowledgementDueDate,
    string? AcknowledgementStatement,
    DateTimeOffset UpdatedAt);

public enum DocumentExpiryStatus
{
    Valid,
    ExpiringSoon,
    Expired,
}
