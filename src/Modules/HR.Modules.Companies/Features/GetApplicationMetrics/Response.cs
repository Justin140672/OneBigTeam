namespace HR.Modules.Companies.Features.GetApplicationMetrics;

internal sealed record DailyMetricPoint(DateOnly Date, int Count);

internal sealed record GetApplicationMetricsResponse(
    // Real historical data (last 30 days, grouped by day, gaps filled with 0).
    IReadOnlyList<DailyMetricPoint> DailySignups,
    IReadOnlyList<DailyMetricPoint> DailyDocumentsUploaded,

    // Snapshot-based trend (accumulates one point per day going forward from today; may be short/
    // empty for new deployments — not gap-filled).
    IReadOnlyList<DailyMetricPoint> ActiveCompaniesTrend,

    // Current-value-only (no meaningful historical trend available).
    int CurrentActiveCompanies,
    int CurrentActiveUsers,
    long CurrentStorageConsumedBytes,
    int CurrentBackgroundJobsSucceededTotal,

    // Documented gap.
    bool EmailsSentTracked,
    string EmailsSentGapReason);
