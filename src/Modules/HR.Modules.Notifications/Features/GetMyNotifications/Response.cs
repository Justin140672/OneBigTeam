namespace HR.Modules.Notifications.Features.GetMyNotifications;

internal sealed record MyNotificationItem(
    Guid Id,
    string Title,
    string? Body,
    bool IsRead,
    Guid SourceEntityId,
    string Type,
    string Priority,
    DateTimeOffset CreatedAt,
    string? ActionUrl);

// NOT-06: UnreadCount is now sourced from an independent, unfiltered, unpaginated COUNT query
// (see GetMyNotificationsHandler) instead of being derived from the current page's Items — it
// always represents every unread notification belonging to the employee, regardless of which
// page or filters produced Items. TotalCount/PageNumber/PageSize/TotalPages mirror DOC-06's
// SearchEmployeeDocumentsResponse pagination shape.
internal sealed record GetMyNotificationsResponse(
    int UnreadCount,
    List<MyNotificationItem> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages);
