namespace HR.Admin.Web.Models;

public sealed record CustomerListResponse(IReadOnlyList<CustomerListItem> Customers);

public sealed record CustomerListItem(
    Guid CompanyId,
    string CompanyName,
    string SubscriptionStatus,
    int CurrentEmployeeCount,
    decimal? MonthlyCharge,
    DateTimeOffset? TrialEndsAt,
    DateTimeOffset CreatedAt);
