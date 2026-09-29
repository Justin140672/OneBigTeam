namespace HR.Modules.Companies.Features.GetCustomerSupportView;

internal sealed record GetCustomerSupportViewResponse(
    Guid CompanyId,
    string CompanyName,
    string Status,

    string SubscriptionStatus,
    DateTimeOffset? TrialStartedAt,
    DateTimeOffset? TrialExpiresAt,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    bool AdminForcedReadOnly,

    // User count (portal/login accounts, identity.user_profiles) vs employee count (HR records) —
    // deliberately distinct concepts, both genuinely available.
    int UserCount,
    int ActiveEmployeeCount,
    int TotalEmployeeCount,

    IReadOnlyList<SupportBillingSnapshotDto> RecentBillingSnapshots,

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

internal sealed record SupportBillingSnapshotDto(
    DateTimeOffset ComputedAt,
    int ChargeableEmployees,
    decimal MonthlyTotal);
