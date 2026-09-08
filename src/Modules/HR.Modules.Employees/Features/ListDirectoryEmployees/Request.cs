namespace HR.Modules.Employees.Features.ListDirectoryEmployees;

internal sealed record ListDirectoryEmployeesRequest
{
    public Guid CompanyId { get; init; }
    public string? Search { get; init; }
    public Guid? DepartmentId { get; init; }
    public Guid? LocationId { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}
