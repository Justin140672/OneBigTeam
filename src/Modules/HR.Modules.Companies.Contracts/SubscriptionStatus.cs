namespace HR.Modules.Companies.Contracts;

// Trial/subscription lifecycle status owned by HR.Modules.Companies' CustomerSubscription
// aggregate. Exposed here (rather than kept module-internal) because it is returned across the
// module boundary by ISubscriptionStatusReader, following the same precedent as
// NoticePeriodUnit/EmployeeUserAccountStatus above.
public enum SubscriptionStatus
{
    Trial = 0,
    TrialExpired = 1,
    Active = 2,
    PastDue = 3,
    Canceled = 4,

    // Ticket 25 (P1): Stripe's "paused" status is a valid, documented subscription state — a
    // subscription can be paused (e.g. via Stripe's pause-collection feature) and later resumed;
    // while paused it generates no invoices. Product decision (made conservatively, without
    // blocking on product input): a paused subscription is treated as READ-ONLY, the same
    // restriction already applied to an expired trial or an admin-forced override (see
    // SubscriptionStatusReader.IsReadOnly) — no invoices are being generated, so full paid access
    // must not be silently granted. It is modelled as its own explicit status (not folded into
    // TrialExpired/Canceled/"inactive") so billing/support screens and status-count reporting can
    // show "Paused" distinctly from those other states, and so a `customer.subscription.resumed`
    // event has an unambiguous prior state to transition out of.
    Paused = 5,
}
