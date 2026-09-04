namespace HR.Modules.Companies.Domain;

/// <summary>
/// OBT-REM-07: a record of a Stripe webhook event whose signature was verified and whose business
/// projection onto <see cref="CustomerSubscription"/> was successfully applied (or deliberately
/// skipped as a stale/duplicate). Persisted in the same transaction as the state change it caused,
/// so a row exists only once the local change has committed.
///
/// <para>
/// A unique index on <see cref="StripeEventId"/> makes redelivery of the same event a safe no-op,
/// and <see cref="EventCreatedAt"/> lets the handler ignore a subscription update that is older than
/// the newest event already applied to that subscription.
/// </para>
/// </summary>
internal sealed class ProcessedStripeEvent
{
    public Guid Id { get; private set; }

    /// <summary>Stripe's own event identifier (<c>evt_...</c>).</summary>
    public string StripeEventId { get; private set; } = string.Empty;

    public string EventType { get; private set; } = string.Empty;

    /// <summary>When Stripe created the event — the ordering key.</summary>
    public DateTimeOffset EventCreatedAt { get; private set; }

    /// <summary>Tenant this event related to, when resolvable.</summary>
    public Guid? CompanyId { get; private set; }

    /// <summary>Stripe subscription the event related to, when present — scopes the ordering check.</summary>
    public string? StripeSubscriptionId { get; private set; }

    /// <summary>Whether the projection was applied (false = recognised but skipped as stale).</summary>
    public bool Applied { get; private set; }

    public DateTimeOffset ProcessedAt { get; private set; }

    private ProcessedStripeEvent() { }

    public static ProcessedStripeEvent Record(
        string stripeEventId,
        string eventType,
        DateTimeOffset eventCreatedAt,
        Guid? companyId,
        string? stripeSubscriptionId,
        bool applied,
        DateTimeOffset processedAt)
        => new()
        {
            Id = Guid.NewGuid(),
            StripeEventId = stripeEventId,
            EventType = eventType,
            EventCreatedAt = eventCreatedAt,
            CompanyId = companyId,
            StripeSubscriptionId = stripeSubscriptionId,
            Applied = applied,
            ProcessedAt = processedAt,
        };
}
