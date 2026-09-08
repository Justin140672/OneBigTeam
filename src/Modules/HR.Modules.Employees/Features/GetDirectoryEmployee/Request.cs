namespace HR.Modules.Employees.Features.GetDirectoryEmployee;

internal sealed record GetDirectoryEmployeeRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
}
