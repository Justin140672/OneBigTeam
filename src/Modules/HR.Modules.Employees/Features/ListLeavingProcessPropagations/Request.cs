namespace HR.Modules.Employees.Features.ListLeavingProcessPropagations;

internal sealed record ListLeavingProcessPropagationsRequest
{
    public Guid CompanyId { get; init; }
    public Guid? EmployeeId { get; init; }
    public string? Status { get; init; }
    public int Limit { get; init; } = 100;
}
