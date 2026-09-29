namespace HR.Modules.Companies.Features.GetApplicationMetrics;

internal sealed record DailyMetricPoint(DateOnly Date, int Count);

internal sealed record GetApplicationMetricsResponse(
    IReadOnlyList<DailyMetricPoint> DailySignups,
    IReadOnlyList<DailyMetricPoint> DailyDocumentsUploaded,

    IReadOnlyList<DailyMetricPoint> ActiveCompaniesTrend,

    int CurrentActiveCompanies,
    int CurrentActiveUsers,
    long CurrentStorageConsumedBytes,
    int CurrentBackgroundJobsSucceededTotal,

    bool EmailsSentTracked,
    string EmailsSentGapReason);
