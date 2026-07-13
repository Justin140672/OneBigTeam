using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.ListEmployees;

internal sealed record ListEmployeesResponse(
    IReadOnlyList<EmployeeListItem> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages);

internal sealed record EmployeeListItem(
    Guid Id,
    Guid CompanyId,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? LocationId,
    string? LocationName,
    Guid? PositionProfileId,
    string? PositionProfileTitle,
    Guid? ManagerId,
    string? ManagerFullName,
    string FirstName,
    string LastName,
    string WorkEmail,
    DateOnly StartDate,
    EmploymentStatus Status,
    DateTimeOffset CreatedAt,
    string? ProfilePhotoUrl);
