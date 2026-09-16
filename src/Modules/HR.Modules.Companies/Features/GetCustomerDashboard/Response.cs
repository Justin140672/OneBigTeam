namespace HR.Modules.Companies.Features.GetCustomerDashboard;

internal sealed record GetCustomerDashboardResponse(
    int TotalCustomers,
    int ActiveCustomers,
    int TrialCustomers,
    int ReadOnlyCustomers,
    // Ticket 25 (P1): paused subscriptions are also read-only (folded into ReadOnlyCustomers
    // above) but are broken out separately here too, since "paused" is a distinct customer
    // lifecycle state from an expired trial and support/dashboards should be able to tell them
    // apart.
    int PausedCustomers,
    int CancelledSubscriptions,
    int PendingPermanentDeletions,
    IReadOnlyList<CustomerDashboardRegistrationDto> RecentRegistrations,
    IReadOnlyList<CustomerDashboardSubscriptionChangeDto> RecentSubscriptionChanges);

internal sealed record CustomerDashboardRegistrationDto(
    Guid CompanyId,
    string CompanyName,
    DateTimeOffset RegisteredAt);

internal sealed record CustomerDashboardSubscriptionChangeDto(
    Guid CompanyId,
    string CompanyName,
    string Status,
    DateTimeOffset ChangedAt);
