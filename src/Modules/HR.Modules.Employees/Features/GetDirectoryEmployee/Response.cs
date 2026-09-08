namespace HR.Modules.Employees.Features.GetDirectoryEmployee;

internal sealed record GetDirectoryEmployeeResponse(
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
    string? WorkPhone,
    DateOnly StartDate,
    Guid? ManagerId,
    string? ManagerFullName,
    string? ProfilePhotoUrl);
