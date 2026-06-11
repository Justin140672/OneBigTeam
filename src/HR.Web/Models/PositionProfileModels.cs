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
    DateTimeOffset UpdatedAt);

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
