namespace HR.Modules.Notifications.Features.MarkNotificationRead;

internal sealed class MarkNotificationReadRequest
{
    public Guid CompanyId { get; init; }
    public Guid NotificationId { get; init; }

    /// <summary>
    /// Resolved server-side from ICurrentUser in the Endpoint — never bound from the route or body.
    /// </summary>
    public Guid EmployeeId { get; init; }
}
