namespace HR.Modules.Employees.Features.GetDepartment;

internal sealed record GetDepartmentResponse(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    Guid? ParentDepartmentId,
    Guid? ManagerEmployeeId,
    bool IsActive);
