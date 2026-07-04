namespace HR.Modules.Employees.Features.GetDepartment;

internal sealed record GetDepartmentRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
}
