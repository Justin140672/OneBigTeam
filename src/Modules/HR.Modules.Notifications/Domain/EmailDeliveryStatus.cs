namespace HR.Modules.Notifications.Domain;

internal enum EmailDeliveryStatus
{
    Pending = 1,
    Sent    = 2,
    Failed  = 3,

    Skipped = 4,

    Sending = 5,
}
