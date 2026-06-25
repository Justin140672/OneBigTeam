namespace HR.Web.Models;

public sealed record NotificationDto(
    Guid Id,
    string Title,
    string? Body,
    bool IsRead,
    Guid SourceEntityId,
    string Type,
    string Priority,
    DateTimeOffset CreatedAt);

public sealed record NotificationsResponse(int UnreadCount, List<NotificationDto> Items);
