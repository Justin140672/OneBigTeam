namespace HR.Modules.Notifications.Domain;

internal enum EmailDeliveryStatus
{
    Pending = 1,
    Sent    = 2,
    Failed  = 3,

    // SET-06: distinct from Failed — this is expected, intended non-delivery because the company
    // disabled email notifications (re-checked at dispatch time by EmailDeliveryJob), not an error.
    // A Skipped row is a final state (never retried) so disabling email never leaves rows
    // indefinitely Pending.
    Skipped = 4,
}
