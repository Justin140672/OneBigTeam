namespace HR.Modules.Companies.Features.GetFailedPayments;

/// <summary>
/// Failed payments are only ever real Stripe invoice data, or explicitly absent — same
/// StripeConfigured convention as GetCustomerBillingHistoryResponse. Never fabricates rows.
/// </summary>
internal sealed record GetFailedPaymentsResponse(
    bool StripeConfigured,
    IReadOnlyList<FailedPaymentDto> FailedPayments);

/// <summary>
/// One row per failed/unpaid Stripe invoice, joined back to local Company/CustomerSubscription data
/// by StripeCustomerId. RetryScheduledAt maps directly to Stripe's own next_payment_attempt (Stripe
/// drives the actual retry schedule, e.g. Smart Retries) — null means Stripe has stopped retrying
/// (typically because the invoice is "uncollectible"). LastSuccessfulPaymentAt/Amount are null when
/// the customer has never had a paid invoice.
/// </summary>
internal sealed record FailedPaymentDto(
    Guid CompanyId,
    string CompanyName,
    string SubscriptionStatus,
    string StripeInvoiceId,
    string InvoiceStatus,
    decimal OutstandingAmount,
    string Currency,
    DateTimeOffset InvoiceDate,
    DateTimeOffset? RetryScheduledAt,
    DateTimeOffset? LastSuccessfulPaymentAt,
    decimal? LastSuccessfulPaymentAmount,
    string? HostedInvoiceUrl);
