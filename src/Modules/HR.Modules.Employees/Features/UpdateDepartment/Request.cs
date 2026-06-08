namespace HR.Modules.Employees.Features.UpdateDepartment;

internal sealed record UpdateDepartmentRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Guid? ParentDepartmentId { get; init; }
    public Guid? ManagerEmployeeId { get; init; }
}
