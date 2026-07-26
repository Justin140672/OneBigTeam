using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.GetEmployeeTimeline;

internal sealed record EmployeeTimelineItem(
    Guid Id,
    DateOnly EventDate,
    EmployeeTimelineEventType EventType,
    EmployeeTimelineCategory Category,
    string Title,
    string Summary,
    string PerformedBy,
    string SourceModule,
    Guid? SourceRecordId);

internal sealed record GetEmployeeTimelineResponse(
    IReadOnlyList<EmployeeTimelineItem> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages);
