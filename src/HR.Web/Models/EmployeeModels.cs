using HR.SharedKernel;

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
    string? DepartmentName,
    Guid? PositionProfileId,
    string? PositionTitle,
    Guid? ManagerId,
    string? ManagerFullName,
    string FirstName,
    string LastName,
    string? PreferredName,
    string WorkEmail,
    string? PersonalEmail,
    DateOnly StartDate,
    DateOnly? DateOfBirth,
    string? Nationality,
    string? Gender,
    string? GenderOther,
    string Status,
    bool HasSystemAccess,
    WorkingDays? WorkingDaysOverride,
    decimal? HoursPerDayOverride,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// ── PERSONAL DETAILS ──────────────────────────────────────────────────────────

public sealed record GetMyPersonalDetailsResponse(
    Guid EmployeeId,
    string FirstName,
    string LastName,
    string? PreferredName,
    DateOnly? DateOfBirth,
    string? Nationality,
    string? Gender);

public sealed record RequestPersonalDetailsChangeRequest(string Notes);

public sealed record RequestPersonalDetailsChangeResponse(Guid TaskId);

// ── EDIT MODELS ───────────────────────────────────────────────────────────────

public sealed class EmployeeProfileEditModel
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string PreferredName { get; set; } = string.Empty;
    public string WorkEmail { get; set; } = string.Empty;
    public string? PersonalEmail { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string Nationality { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string GenderOther { get; set; } = string.Empty;
    public Guid? DepartmentId { get; set; }
    public Guid? PositionProfileId { get; set; }
    public bool HasSystemAccess { get; set; } = true;
    public bool OverrideWorkingPattern { get; set; } = false;
    public HashSet<string> WorkingWeek { get; set; } = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"];
    public decimal HoursPerDay { get; set; } = 7.5m;
}

public record UpdateEmployeeProfileRequest(
    Guid CompanyId,
    Guid Id,
    Guid? DepartmentId,
    Guid? PositionProfileId,
    string FirstName,
    string LastName,
    string? PreferredName,
    string WorkEmail,
    string? PersonalEmail,
    DateOnly StartDate,
    DateOnly? DateOfBirth,
    string? Nationality,
    string? Gender,
    string? GenderOther,
    bool HasSystemAccess,
    WorkingDays? WorkingDaysOverride,
    decimal? HoursPerDayOverride);

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
    string? PreferredName,
    string WorkEmail,
    string? PersonalEmail,
    DateOnly StartDate,
    DateOnly DateOfBirth,
    string Nationality,
    string Gender,
    string? GenderOther,
    bool HasSystemAccess);

public record CreateEmployeeResponse(
    Guid Id,
    Guid CompanyId,
    string FirstName,
    string LastName,
    string WorkEmail,
    string Status,
    DateTimeOffset CreatedAt);

// ── NATIONALITIES ─────────────────────────────────────────────────────────────

public record ListNationalitiesResponse(IReadOnlyList<NationalityListItem> Items);

public record NationalityListItem(int Id, string Name);

