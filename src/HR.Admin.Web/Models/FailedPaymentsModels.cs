namespace HR.Admin.Web.Models;

// Mirrors HR.Modules.Companies.Features.GetFailedPayments.Response exactly — same
// "app-local DTO matching the API contract" convention as CustomerBillingHistoryModels.cs.
public sealed record FailedPaymentsResponse(
    bool StripeConfigured,
    IReadOnlyList<FailedPaymentItem> FailedPayments);

public sealed record FailedPaymentItem(
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
