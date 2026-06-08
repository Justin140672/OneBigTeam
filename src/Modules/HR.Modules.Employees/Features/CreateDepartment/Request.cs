namespace HR.Modules.Employees.Features.CreateDepartment;

internal sealed record CreateDepartmentRequest
{
    public Guid CompanyId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Guid? ParentDepartmentId { get; init; }
}
