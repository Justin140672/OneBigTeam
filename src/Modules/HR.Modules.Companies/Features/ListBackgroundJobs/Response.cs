namespace HR.Modules.Companies.Features.ListBackgroundJobs;

internal sealed record ListBackgroundJobsResponse(
    bool Available,
    IReadOnlyList<BackgroundJobItem> Scheduled,
    IReadOnlyList<BackgroundJobItem> Running,
    IReadOnlyList<BackgroundJobItem> Failed);

internal sealed record BackgroundJobItem(
    string JobId,
    string JobName,
    string State,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? LastExecutedAt,
    DateTimeOffset? NextExecutionAt,
    int RetryCount,
    string? FailureReason);
