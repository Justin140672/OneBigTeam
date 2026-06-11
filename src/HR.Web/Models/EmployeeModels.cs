namespace HR.Web.Models;

// ── LIST ──────────────────────────────────────────────────────────────────────

public record ListEmployeesResponse(
    List<EmployeeListItemModel> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages);

public record EmployeeListItemModel(
    Guid Id,
    Guid CompanyId,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? PositionProfileId,
    string? PositionProfileTitle,
    Guid? ManagerId,
    string? ManagerFullName,
    string FirstName,
    string LastName,
    string WorkEmail,
    DateOnly StartDate,
    string Status,
    DateTimeOffset CreatedAt);

// ── GET ───────────────────────────────────────────────────────────────────────

public record GetEmployeeResponse(
    Guid Id,
    Guid CompanyId,
    Guid? DepartmentId,
    Guid? PositionProfileId,
    Guid? ManagerId,
    string FirstName,
    string LastName,
    string WorkEmail,
    string? PersonalEmail,
    DateOnly StartDate,
    string Status,
    bool HasSystemAccess,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// ── EDIT MODELS ───────────────────────────────────────────────────────────────

public sealed class EmployeeProfileEditModel
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string WorkEmail { get; set; } = string.Empty;
    public string? PersonalEmail { get; set; }
    public DateOnly StartDate { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? PositionProfileId { get; set; }
    public bool HasSystemAccess { get; set; } = true;
}

public record UpdateEmployeeProfileRequest(
    Guid CompanyId,
    Guid Id,
    Guid? DepartmentId,
    Guid? PositionProfileId,
    string FirstName,
    string LastName,
    string WorkEmail,
    string? PersonalEmail,
    DateOnly StartDate,
    bool HasSystemAccess);

public record UpdateEmployeeProfileResponse(
    Guid Id,
    Guid CompanyId,
    Guid? DepartmentId,
    string FirstName,
    string LastName,
    string WorkEmail,
    string? PersonalEmail,
    DateOnly StartDate,
    string Status,
    DateTimeOffset UpdatedAt);

// ── CREATE ────────────────────────────────────────────────────────────────────

public sealed class CreateEmployeeFormModel
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string WorkEmail { get; set; } = string.Empty;
    public string? PersonalEmail { get; set; }
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
}

public record CreateEmployeeRequest(
    Guid CompanyId,
    Guid? DepartmentId,
    Guid? PositionProfileId,
    string FirstName,
    string LastName,
    string WorkEmail,
    string? PersonalEmail,
    DateOnly StartDate,
    bool HasSystemAccess);

public record CreateEmployeeResponse(
    Guid Id,
    Guid CompanyId,
    string FirstName,
    string LastName,
    string WorkEmail,
    string Status,
    DateTimeOffset CreatedAt);
