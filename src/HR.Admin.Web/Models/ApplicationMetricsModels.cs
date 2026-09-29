namespace HR.Admin.Web.Models;

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
