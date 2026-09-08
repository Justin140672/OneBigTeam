namespace HR.Modules.Employees.Features.ListDirectoryEmployees;

internal sealed record ListDirectoryEmployeesResponse(
    IReadOnlyList<DirectoryEmployeeListItem> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages);

internal sealed record DirectoryEmployeeListItem(
    Guid Id,
    string FirstName,
    string LastName,
    string? PreferredName,
    string? PositionTitle,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? LocationId,
    string? LocationName,
    string WorkEmail,
    string? ProfilePhotoUrl);
