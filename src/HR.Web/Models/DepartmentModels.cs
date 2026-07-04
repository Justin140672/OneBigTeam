using System.ComponentModel.DataAnnotations;

namespace HR.Web.Models;

// ── LIST ──────────────────────────────────────────────────────────────────────

public record ListDepartmentsResponse(List<DepartmentListItemModel> Items);

public record DepartmentListItemModel(
    Guid Id,
    string Name,
    Guid? ParentDepartmentId,
    Guid? ManagerEmployeeId,
    bool IsActive);

// ── GET ───────────────────────────────────────────────────────────────────────

public record GetDepartmentResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    Guid? ParentDepartmentId,
    Guid? ManagerEmployeeId,
    bool IsActive);

// ── CREATE ────────────────────────────────────────────────────────────────────

public record CreateDepartmentRequest(
    Guid CompanyId,
    string Name,
    string? Description,
    Guid? ParentDepartmentId);

public record CreateDepartmentResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    Guid? ParentDepartmentId,
    bool IsActive,
    DateTimeOffset CreatedAt);

// ── UPDATE ────────────────────────────────────────────────────────────────────

public record UpdateDepartmentRequest(
    Guid CompanyId,
    Guid Id,
    string Name,
    string? Description,
    Guid? ParentDepartmentId,
    Guid? ManagerEmployeeId);

public record UpdateDepartmentResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    Guid? ParentDepartmentId,
    Guid? ManagerEmployeeId,
    bool IsActive,
    DateTimeOffset UpdatedAt);

// ── EDIT MODEL ────────────────────────────────────────────────────────────────

public sealed class DepartmentEditModel
{
    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentDepartmentId { get; set; }
    public Guid? ManagerEmployeeId { get; set; }
}
