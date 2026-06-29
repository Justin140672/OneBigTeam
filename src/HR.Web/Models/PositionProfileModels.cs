namespace HR.Web.Models;

// ── LIST ──────────────────────────────────────────────────────────────────────

public record ListPositionProfilesResponse(
    IReadOnlyList<PositionProfileListItemModel> Items);

public record PositionProfileListItemModel(
    Guid Id,
    string? DepartmentName,
    string Title,
    string? Description,
    bool IsManagerial,
    bool IsActive);

// ── GET ───────────────────────────────────────────────────────────────────────

public record GetPositionProfileResponse(
    Guid Id,
    Guid CompanyId,
    Guid? DepartmentId,
    string Title,
    string? Description,
    bool IsManagerial,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<PositionProfileRequiredDocumentModel> RequiredDocuments);

public record PositionProfileRequiredDocumentModel(
    Guid Id,
    Guid DocumentTypeId,
    bool IsMandatory,
    int? DueDaysAfterStart,
    bool RequiresExpiryDate);

// ── EDIT MODEL ────────────────────────────────────────────────────────────────

public sealed class PositionProfileEditModel
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? DepartmentId { get; set; }
    public bool IsManagerial { get; set; }
}

// ── CREATE ────────────────────────────────────────────────────────────────────

public record CreatePositionProfileRequest(
    Guid CompanyId,
    Guid? DepartmentId,
    string Title,
    string? Description,
    bool IsManagerial);

public record CreatePositionProfileResponse(
    Guid Id,
    Guid CompanyId,
    Guid? DepartmentId,
    string Title,
    string? Description,
    bool IsManagerial,
    bool IsActive,
    DateTimeOffset CreatedAt);

// ── LIST REQUIRED DOCUMENTS ───────────────────────────────────────────────────

public record ListRequiredDocumentsResponse(IReadOnlyList<RequiredDocumentListItemModel> Items);

public record RequiredDocumentListItemModel(
    Guid Id,
    Guid DocumentTypeId,
    string DocumentTypeName,
    bool IsMandatory,
    int? DueDaysAfterStart,
    bool RequiresExpiryDate);

// ── ADD / REMOVE REQUIRED DOCUMENT ────────────────────────────────────────────

public record AddRequiredDocumentToProfileRequest(
    Guid CompanyId,
    Guid PositionProfileId,
    Guid DocumentTypeId,
    bool IsMandatory,
    int? DueDaysAfterStart,
    bool RequiresExpiryDate);

public record AddRequiredDocumentToProfileResponse(Guid Id);

// ── UPDATE ────────────────────────────────────────────────────────────────────

public record UpdatePositionProfileRequest(
    Guid CompanyId,
    Guid Id,
    Guid? DepartmentId,
    string Title,
    string? Description,
    bool IsManagerial);

public record UpdatePositionProfileResponse(
    Guid Id,
    Guid CompanyId,
    Guid? DepartmentId,
    string Title,
    string? Description,
    bool IsManagerial,
    bool IsActive,
    DateTimeOffset UpdatedAt);
