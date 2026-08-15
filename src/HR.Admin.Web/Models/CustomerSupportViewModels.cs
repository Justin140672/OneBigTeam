namespace HR.Admin.Web.Models;

// Mirrors HR.Modules.Companies.Features.GetCustomerSupportView.Response's shape exactly — same
// "app-local DTO matching the API contract" convention as CustomerDetailsModels.cs.
public sealed record CustomerSupportViewResponse(
    Guid CompanyId,
    string CompanyName,
    string Status,

    string SubscriptionStatus,
    DateTimeOffset? TrialStartedAt,
    DateTimeOffset? TrialExpiresAt,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    bool AdminForcedReadOnly,

    int UserCount,
    int ActiveEmployeeCount,
    int TotalEmployeeCount,

    IReadOnlyList<SupportBillingSnapshot> RecentBillingSnapshots,

    bool BackgroundJobsAvailable,
    int BackgroundJobServerCount,
    int BackgroundJobsEnqueued,
    int BackgroundJobsProcessing,
    int BackgroundJobsScheduled,
    int BackgroundJobsFailed,
    int BackgroundJobsSucceeded,
    int BackgroundJobsRecurring,

    bool RecentErrorsAvailable,
    bool RecentEmailsAvailable,
    bool RecentLoginActivityAvailable);

public sealed record SupportBillingSnapshot(
    DateTimeOffset ComputedAt,
    int ChargeableEmployees,
    decimal MonthlyTotal);
