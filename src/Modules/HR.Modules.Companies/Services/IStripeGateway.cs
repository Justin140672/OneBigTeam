namespace HR.Modules.Companies.Services;

internal interface IStripeGateway
{
    Task<string> CreateCheckoutSessionAsync(
        Guid companyId,
        string customerEmail,
        string? existingStripeCustomerId,
        string successUrl,
        string cancelUrl,
        CancellationToken cancellationToken);

    StripeWebhookEvent ConstructAndParseWebhookEvent(string payload, string signatureHeader);

    Task CancelSubscriptionAsync(
        string stripeSubscriptionId,
        bool atPeriodEnd,
        CancellationToken cancellationToken);

    Task ResumeSubscriptionAsync(string stripeSubscriptionId, CancellationToken cancellationToken);

    Task<string> CreateBillingPortalSessionAsync(
        string stripeCustomerId,
        string returnUrl,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StripeInvoiceSummary>> ListInvoicesAsync(
        string stripeCustomerId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FailedInvoiceSummary>> ListFailedInvoicesAsync(CancellationToken cancellationToken);

    Task<StripeInvoiceSummary?> GetMostRecentPaidInvoiceAsync(
        string stripeCustomerId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Ticket 9 (P2): fetches the CURRENT, authoritative state of a Stripe subscription directly
    /// from Stripe's API — used by StripeWebhookHandler to reconcile ambiguous webhook deliveries
    /// (two different events for the same subscription sharing the same creation timestamp, so
    /// there is no reliable ordering between them) instead of treating an arbitrary event-id
    /// tie-break as if it were chronological. Returns null if Stripe reports no such subscription.
    /// </summary>
    Task<StripeSubscriptionSnapshot?> GetSubscriptionAsync(
        string stripeSubscriptionId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Ticket 9 (P2): a thin, authoritative snapshot of a live Stripe subscription's current state,
/// fetched on demand rather than derived from any single webhook payload.
/// </summary>
internal sealed record StripeSubscriptionSnapshot(
    string StripeSubscriptionId,
    string StripeCustomerId,
    string Status,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    string? PriceId);

internal sealed record FailedInvoiceSummary(
    string Id,
    string StripeCustomerId,
    DateTimeOffset InvoiceDate,
    decimal OutstandingAmount,
    string Currency,
    string Status,
    DateTimeOffset? NextPaymentAttempt,
    string? HostedInvoiceUrl);

internal sealed record StripeInvoiceSummary(
    string Id,
    DateTimeOffset InvoiceDate,
    decimal Amount,
    string Currency,
    string Status,
    DateTimeOffset? PaidAt,
    string? HostedInvoiceUrl);
