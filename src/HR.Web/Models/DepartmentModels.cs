using System.ComponentModel.DataAnnotations;
using HR.Web.Services;

namespace HR.Web.Models;

// ── LIST ──────────────────────────────────────────────────────────────────────

public record ListDepartmentsResponse(List<DepartmentListItemModel> Items);

public record DepartmentListItemModel(
    Guid Id,
    string Name,
    Guid? ParentDepartmentId,
    Guid? ManagerEmployeeId,
    bool IsActive,
    int Version = 0);

// ── GET ───────────────────────────────────────────────────────────────────────

public record GetDepartmentResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    Guid? ParentDepartmentId,
    Guid? ManagerEmployeeId,
    bool IsActive,
    int Version = 0);

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
    Guid? ManagerEmployeeId,
    // Ticket 2: optimistic-concurrency token loaded before editing.
    int? ExpectedVersion = null);

public record UpdateDepartmentResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    Guid? ParentDepartmentId,
    Guid? ManagerEmployeeId,
    bool IsActive,
    DateTimeOffset UpdatedAt,
    int Version = 0);

// ── EDIT MODEL ────────────────────────────────────────────────────────────────

public sealed class DepartmentEditModel : IHasVersion
{
    public int Version { get; set; }

    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentDepartmentId { get; set; }
    public Guid? ManagerEmployeeId { get; set; }
}
