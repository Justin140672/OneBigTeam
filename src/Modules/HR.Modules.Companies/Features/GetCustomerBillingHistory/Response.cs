namespace HR.Modules.Companies.Features.GetCustomerBillingHistory;

/// <summary>
/// Billing history is only ever real Stripe invoice data, or explicitly absent — this endpoint
/// never fabricates invoice rows. <see cref="StripeConfigured"/> and <see cref="HasStripeCustomer"/>
/// let the UI distinguish "no API key configured" (e.g. local/dev) from "this customer has never
/// checked out through Stripe" from "Stripe has no invoices for this customer yet", all of which are
/// legitimate reasons <see cref="Invoices"/> can be empty.
/// </summary>
internal sealed record GetCustomerBillingHistoryResponse(
    Guid CompanyId,
    bool StripeConfigured,
    bool HasStripeCustomer,
    IReadOnlyList<BillingHistoryInvoiceDto> Invoices);

/// <summary>
/// Maps directly to real fields returned by the Stripe Invoices API for the customer's
/// StripeCustomerId — nothing here is computed or invented locally, except
/// <see cref="EstimatedEmployeeCount"/>, which Stripe does not track (checkout always uses a fixed
/// line-item quantity of 1 regardless of headcount — see StripeGateway.CreateCheckoutSessionAsync)
/// and is instead approximated from the closest recorded CustomerBillingSnapshot at or before the
/// invoice date, when one exists.
/// </summary>
internal sealed record BillingHistoryInvoiceDto(
    string StripeInvoiceId,
    DateTimeOffset InvoiceDate,
    decimal Amount,
    string Currency,
    int? EstimatedEmployeeCount,
    string PaymentStatus,
    DateTimeOffset? PaymentDate,
    string? HostedInvoiceUrl);
