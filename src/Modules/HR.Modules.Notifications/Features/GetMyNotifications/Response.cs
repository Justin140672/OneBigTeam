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

internal sealed record GetMyNotificationsResponse(
    int UnreadCount,
    List<MyNotificationItem> Items);
