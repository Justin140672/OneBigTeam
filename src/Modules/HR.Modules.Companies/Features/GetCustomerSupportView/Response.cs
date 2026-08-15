namespace HR.Modules.Companies.Features.GetCustomerSupportView;

/// <summary>
/// Condensed, troubleshooting-optimised summary for support staff — the Customer Support View
/// (Support epic). Aggregates data already available elsewhere (subscription/trial/employee counts
/// via the same sources as GetCustomerDetails, portal user count via the new
/// ICompanyUserCountReader, and recent billing snapshots as the closest available substitute for
/// "recent invoices" — no real invoice/payment record exists anywhere in the codebase).
///
/// Four data points from the story's acceptance criteria are genuine platform gaps with no backing
/// data source (verified — no error/exception log table, no sent-email record/outbox, no login
/// audit trail, and background jobs are not tagged per-company). Each is represented as an
/// "*Available" flag set to false; HR.Admin.Web renders an explicit "not yet available" panel for
/// each rather than an empty-looking blank, so support staff aren't misled into thinking the data
/// was checked and came back empty.
/// </summary>
internal sealed record GetCustomerSupportViewResponse(
    Guid CompanyId,
    string CompanyName,
    string Status,

    // Subscription / trial.
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

    // Recent invoices — no real invoice/payment record exists; this is the closest available
    // substitute (the same persisted billing-snapshot history used on Customer Details).
    IReadOnlyList<SupportBillingSnapshotDto> RecentBillingSnapshots,

    // Outstanding background jobs — platform-wide only, see IBackgroundJobStatusReader remarks.
    bool BackgroundJobsAvailable,
    int BackgroundJobServerCount,
    int BackgroundJobsEnqueued,
    int BackgroundJobsProcessing,
    int BackgroundJobsScheduled,
    int BackgroundJobsFailed,
    int BackgroundJobsSucceeded,
    int BackgroundJobsRecurring,

    // Genuine gaps — no backing data source exists yet (see class remarks above).
    bool RecentErrorsAvailable,
    bool RecentEmailsAvailable,
    bool RecentLoginActivityAvailable);

internal sealed record SupportBillingSnapshotDto(
    DateTimeOffset ComputedAt,
    int ChargeableEmployees,
    decimal MonthlyTotal);
