namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Delivery channel(s) a notification should be sent through. In-app is always written by
/// INotificationWriter regardless of this value (it is the existing baseline behaviour); this
/// flag additionally controls whether an asynchronous email delivery is also queued (NOT-02).
/// </summary>
[Flags]
public enum NotificationChannel
{
    InApp = 1,
    Email = 2,
    Both  = InApp | Email,
}
