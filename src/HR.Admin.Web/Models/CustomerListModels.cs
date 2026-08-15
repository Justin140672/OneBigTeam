namespace HR.Admin.Web.Models;

// Mirrors HR.Modules.Companies.Features.ListCustomers.Response's shape exactly — same
// "app-local DTO matching the API contract" convention as CustomerDashboardModels.cs.
public sealed record CustomerListResponse(IReadOnlyList<CustomerListItem> Customers);

public sealed record CustomerListItem(
    Guid CompanyId,
    string CompanyName,
    string SubscriptionStatus,
    int CurrentEmployeeCount,
    decimal? MonthlyCharge,
    DateTimeOffset? TrialEndsAt,
    DateTimeOffset CreatedAt);
