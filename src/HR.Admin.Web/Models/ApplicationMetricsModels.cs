namespace HR.Admin.Web.Models;

// Mirrors HR.Modules.Companies's GetApplicationMetrics response shape exactly — same
// "app-local DTO matching the API contract" convention as SystemHealthResponse etc.
public sealed record DailyMetricPoint(DateOnly Date, int Count);

public sealed record ApplicationMetricsResponse(
    IReadOnlyList<DailyMetricPoint> DailySignups,
    IReadOnlyList<DailyMetricPoint> DailyDocumentsUploaded,
    IReadOnlyList<DailyMetricPoint> ActiveCompaniesTrend,
    int CurrentActiveCompanies,
    int CurrentActiveUsers,
    long CurrentStorageConsumedBytes,
    int CurrentBackgroundJobsSucceededTotal,
    bool EmailsSentTracked,
    string EmailsSentGapReason);
