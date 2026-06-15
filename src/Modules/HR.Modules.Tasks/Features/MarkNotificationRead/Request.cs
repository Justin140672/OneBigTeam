namespace HR.Modules.Tasks.Features.MarkNotificationRead;

internal sealed class MarkNotificationReadRequest
{
    public Guid CompanyId { get; init; }
    public Guid NotificationId { get; init; }
}
