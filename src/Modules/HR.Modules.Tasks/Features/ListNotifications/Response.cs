namespace HR.Modules.Tasks.Features.ListNotifications;

internal sealed record NotificationItem(
    Guid Id,
    string Title,
    string? Body,
    bool IsRead,
    Guid SourceEntityId,
    string Type,
    DateTimeOffset CreatedAt);

internal sealed record ListNotificationsResponse(
    int UnreadCount,
    List<NotificationItem> Items);
