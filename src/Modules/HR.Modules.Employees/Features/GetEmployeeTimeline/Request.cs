using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.GetEmployeeTimeline;

internal sealed record GetEmployeeTimelineRequest
{
    public Guid CompanyId { get; init; }
    public Guid EmployeeId { get; init; }
    public EmployeeTimelineCategory? Category { get; init; }
    public DateOnly? DateFrom { get; init; }
    public DateOnly? DateTo { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
