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

    /// <summary>
    /// Lists real Stripe invoices for a customer (Billing History epic), newest first. Returns an
    /// empty list rather than throwing when the customer has no invoices — callers distinguish "no
    /// Stripe customer" / "no API key configured" upstream before calling this.
    /// </summary>
    Task<IReadOnlyList<StripeInvoiceSummary>> ListInvoicesAsync(
        string stripeCustomerId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists failed/unpaid invoices across the entire Stripe account (Failed Payments Dashboard
    /// epic) — i.e. invoices with status "open" (payment attempted/scheduled, not yet paid) or
    /// "uncollectible" (Stripe has given up retrying), rather than iterating every customer's
    /// invoices individually. This is two account-wide List calls (one per status, since
    /// Stripe.net's InvoiceListOptions.Status only accepts a single value), each auto-paginating
    /// through Stripe.net's built-in enumerable, instead of N calls (one per customer with a
    /// StripeCustomerId) — the efficient option documented in the story brief. Callers join the
    /// returned StripeCustomerId back to CustomerSubscription.StripeCustomerId locally to resolve
    /// company/customer identity, since Stripe has no knowledge of our tenant model.
    /// </summary>
    Task<IReadOnlyList<FailedInvoiceSummary>> ListFailedInvoicesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns the most recent fully-paid invoice for a customer (for "last successful payment" on
    /// the Failed Payments Dashboard), or null if the customer has never had a paid invoice. Only
    /// called per-customer for the (expected to be small) set of customers who currently have a
    /// failed payment — not for every customer in the account — so this remains cheap in practice
    /// despite being a per-customer call.
    /// </summary>
    Task<StripeInvoiceSummary?> GetMostRecentPaidInvoiceAsync(
        string stripeCustomerId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Thin projection of the Stripe Invoice fields the Failed Payments Dashboard needs. NextPaymentAttempt
/// maps directly to Stripe's own retry-schedule field — Stripe (not this app) owns and drives the
/// Smart Retries schedule; this is a read-only reflection of it.
/// </summary>
internal sealed record FailedInvoiceSummary(
    string Id,
    string StripeCustomerId,
    DateTimeOffset InvoiceDate,
    decimal OutstandingAmount,
    string Currency,
    string Status,
    DateTimeOffset? NextPaymentAttempt,
    string? HostedInvoiceUrl);

/// <summary>
/// Thin projection of the Stripe Invoice fields the Billing History admin page needs, so
/// GetCustomerBillingHistoryHandler doesn't take a dependency on the Stripe.net Invoice type
/// directly.
/// </summary>
internal sealed record StripeInvoiceSummary(
    string Id,
    DateTimeOffset InvoiceDate,
    decimal Amount,
    string Currency,
    string Status,
    DateTimeOffset? PaidAt,
    string? HostedInvoiceUrl);
