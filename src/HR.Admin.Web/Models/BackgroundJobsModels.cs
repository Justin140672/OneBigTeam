namespace HR.Admin.Web.Models;

// Mirrors HR.Modules.Companies.Features.ListBackgroundJobs.Response exactly — same
// "app-local DTO matching the API contract" convention as FailedPaymentsModels.cs.
public sealed record BackgroundJobsResponse(
    bool Available,
    IReadOnlyList<BackgroundJobItem> Scheduled,
    IReadOnlyList<BackgroundJobItem> Running,
    IReadOnlyList<BackgroundJobItem> Failed);

public sealed record BackgroundJobItem(
    string JobId,
    string JobName,
    string State,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? LastExecutedAt,
    DateTimeOffset? NextExecutionAt,
    int RetryCount,
    string? FailureReason);

public sealed record RetryBackgroundJobResponse(string JobId, bool Success);
