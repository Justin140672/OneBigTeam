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

    // Follow-up E: transient "a worker is actively sending this right now" claim state, used only by
    // OperationalAlertEmailDelivery. A row is moved to Sending under an optimistic-concurrency claim
    // together with a bounded ownership lease before the Postmark call; a second worker cannot claim
    // it while the lease is live. If the owner crashes mid-send the lease expires and the
    // reconciliation sweep (or a Hangfire retry) re-claims it. EmailDelivery never uses this value.
    Sending = 5,
}
