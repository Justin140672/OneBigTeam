namespace HR.Modules.Companies.Features.GetCustomerBillingHistory;

internal sealed record GetCustomerBillingHistoryResponse(
    Guid CompanyId,
    bool StripeConfigured,
    bool HasStripeCustomer,
    IReadOnlyList<BillingHistoryInvoiceDto> Invoices);

internal sealed record BillingHistoryInvoiceDto(
    string StripeInvoiceId,
    DateTimeOffset InvoiceDate,
    decimal Amount,
    string Currency,
    int? EstimatedEmployeeCount,
    string PaymentStatus,
    DateTimeOffset? PaymentDate,
    string? HostedInvoiceUrl);
