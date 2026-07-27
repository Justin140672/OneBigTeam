namespace HR.Web.Models;

// Mirrors HR.Modules.Employees.Features.GetEmployeeTimeline.EmployeeTimelineItem /
// GetEmployeeTimelineResponse. EventType and Category are kept as plain strings here (rather than
// referencing the module's internal enums, which HR.Web cannot see anyway) — they arrive as
// JsonStringEnumConverter-serialized strings and are matched against known values in
// EmployeeTimelineTab's icon/navigation mapping.
public record EmployeeTimelineItemModel(
    Guid Id,
    DateOnly EventDate,
    string EventType,
    string Category,
    string Title,
    string Summary,
    string PerformedBy,
    string SourceModule,
    Guid? SourceRecordId);

public record GetEmployeeTimelineResponse(
    IReadOnlyList<EmployeeTimelineItemModel> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages);
