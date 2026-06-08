using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.ListEmployees;

internal sealed record ListEmployeesRequest
{
    public Guid CompanyId { get; init; }
    public string? Search { get; init; }
    public Guid? DepartmentId { get; init; }
    public Guid? PositionProfileId { get; init; }
    public EmploymentStatus? Status { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
