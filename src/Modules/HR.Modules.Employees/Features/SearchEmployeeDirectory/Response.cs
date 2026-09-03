namespace HR.Modules.Employees.Features.SearchEmployeeDirectory;

internal sealed record SearchEmployeeDirectoryResponse(IReadOnlyList<SearchEmployeeDirectoryItem> Items);

internal sealed record SearchEmployeeDirectoryItem(
    Guid Id,
    string FirstName,
    string LastName,
    string? EmployeeNumber,
    string? PositionProfileTitle,
    string? DepartmentName,
    HR.Modules.Employees.Domain.EmploymentStatus Status);
