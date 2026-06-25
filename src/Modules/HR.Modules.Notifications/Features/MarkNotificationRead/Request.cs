namespace HR.Modules.Notifications.Features.MarkNotificationRead;

internal sealed class MarkNotificationReadRequest
{
    public Guid CompanyId { get; init; }
    public Guid NotificationId { get; init; }
}
