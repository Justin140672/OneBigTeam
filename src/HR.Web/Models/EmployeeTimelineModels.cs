namespace HR.Web.Models;

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
