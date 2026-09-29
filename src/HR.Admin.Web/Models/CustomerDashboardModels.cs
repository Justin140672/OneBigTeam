namespace HR.Admin.Web.Models;

public sealed record CustomerDashboardResponse(
    int TotalCustomers,
    int ActiveCustomers,
    int TrialCustomers,
    int ReadOnlyCustomers,
    int PausedCustomers,
    int CancelledSubscriptions,
    int PendingPermanentDeletions,
    IReadOnlyList<CustomerDashboardRegistration> RecentRegistrations,
    IReadOnlyList<CustomerDashboardSubscriptionChange> RecentSubscriptionChanges);

public sealed record CustomerDashboardRegistration(
    Guid CompanyId,
    string CompanyName,
    DateTimeOffset RegisteredAt);

public sealed record CustomerDashboardSubscriptionChange(
    Guid CompanyId,
    string CompanyName,
    string Status,
    DateTimeOffset ChangedAt)
{
    public string StatusText => HR.SharedKernel.EnumText.Humanize(Status);
}
