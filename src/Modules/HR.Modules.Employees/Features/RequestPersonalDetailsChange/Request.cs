namespace HR.Modules.Employees.Features.RequestPersonalDetailsChange;

internal sealed record RequestPersonalDetailsChangeRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public string Notes { get; init; } = string.Empty;
}
