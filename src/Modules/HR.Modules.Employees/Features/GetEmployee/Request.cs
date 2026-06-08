namespace HR.Modules.Employees.Features.GetEmployee;

internal sealed record GetEmployeeRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
}
