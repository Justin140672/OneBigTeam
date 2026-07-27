namespace HR.Modules.Employees.Features.GetEmployeeTimeline;

internal sealed record GetEmployeeTimelineRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
