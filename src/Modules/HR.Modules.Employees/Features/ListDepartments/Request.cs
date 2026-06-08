namespace HR.Modules.Employees.Features.ListDepartments;

internal sealed record ListDepartmentsRequest
{
    public Guid CompanyId { get; init; }
    public bool IncludeInactive { get; init; } = false;
}
