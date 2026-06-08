namespace HR.Modules.Employees.Features.UpdateDepartment;

internal sealed record UpdateDepartmentResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    Guid? ParentDepartmentId,
    Guid? ManagerEmployeeId,
    bool IsActive,
    DateTimeOffset UpdatedAt);
