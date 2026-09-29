namespace HR.Modules.Companies.Features.GetFailedPayments;

internal sealed record GetFailedPaymentsResponse(
    bool StripeConfigured,
    IReadOnlyList<FailedPaymentDto> FailedPayments);

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
