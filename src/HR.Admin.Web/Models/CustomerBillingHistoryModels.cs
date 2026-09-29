namespace HR.Admin.Web.Models;

public sealed record CustomerBillingHistoryResponse(
    Guid CompanyId,
    bool StripeConfigured,
    bool HasStripeCustomer,
    IReadOnlyList<BillingHistoryInvoiceDto> Invoices);

public sealed record BillingHistoryInvoiceDto(
    string StripeInvoiceId,
    DateTimeOffset InvoiceDate,
    decimal Amount,
    string Currency,
    int? EstimatedEmployeeCount,
    string PaymentStatus,
    DateTimeOffset? PaymentDate,
    string? HostedInvoiceUrl);
