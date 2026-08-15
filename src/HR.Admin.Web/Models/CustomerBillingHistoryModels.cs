namespace HR.Admin.Web.Models;

// Mirrors HR.Modules.Companies.Features.GetCustomerBillingHistory.Response exactly — same
// "app-local DTO matching the API contract" convention as CustomerBillingBreakdownModels.cs.
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
