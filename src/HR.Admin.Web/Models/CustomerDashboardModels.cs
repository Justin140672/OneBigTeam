namespace HR.Admin.Web.Models;

// Mirrors HR.Modules.Companies.Features.GetCustomerDashboard.Response's shape exactly — HR.Web
// follows the same "app-local DTO matching the API contract" convention for every *Models.cs file
// (see HR.Web.Models.CompanyModels etc.), so no shared contracts project is introduced here.
public sealed record CustomerDashboardResponse(
    int TotalCustomers,
    int ActiveCustomers,
    int TrialCustomers,
    int ReadOnlyCustomers,
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
    DateTimeOffset ChangedAt);
