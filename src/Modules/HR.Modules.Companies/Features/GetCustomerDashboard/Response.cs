namespace HR.Modules.Companies.Features.GetCustomerDashboard;

internal sealed record GetCustomerDashboardResponse(
    int TotalCustomers,
    int ActiveCustomers,
    int TrialCustomers,
    int ReadOnlyCustomers,
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
