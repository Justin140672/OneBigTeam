namespace HR.Modules.Employees.Features.ListDepartments;

internal sealed record ListDepartmentsResponse(IReadOnlyList<DepartmentListItem> Items);

internal sealed record DepartmentListItem(
    Guid Id,
    string Name,
    Guid? ParentDepartmentId,
    Guid? ManagerEmployeeId,
    bool IsActive);
