namespace HR.Modules.Employees.Features.DeactivateDepartment;

internal sealed record DeactivateDepartmentRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
}
