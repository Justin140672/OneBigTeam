namespace HR.Web.Models;

public sealed record NotificationDto(
    Guid Id,
    string Title,
    string? Body,
    bool IsRead,
    Guid SourceEntityId,
    string Type,
    string Priority,
    DateTimeOffset CreatedAt,
    string? ActionUrl);

// NOT-06: TotalCount/PageNumber/PageSize/TotalPages support progressive "load more" loading in the
// notification dropdown. UnreadCount is independent of pagination/filters — always the employee's
// full unread total.
public sealed record NotificationsResponse(
    int UnreadCount,
    List<NotificationDto> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages);
