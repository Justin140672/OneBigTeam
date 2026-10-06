using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HR.Modules.Documents.Services;

internal sealed class FileScanBacklogHealthCheck(FileScanBacklogReader reader) : IHealthCheck
{
    internal static readonly TimeSpan DegradedPendingAge = TimeSpan.FromMinutes(15);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var backlog = await reader.ReadAsync(cancellationToken);

        var data = new Dictionary<string, object>
        {
            ["pendingCount"] = backlog.PendingCount,
            ["oldestPendingAgeSeconds"] = backlog.OldestPendingAgeSeconds,
            ["scanningCount"] = backlog.ScanningCount,
            ["staleClaimCount"] = backlog.StaleClaimCount,
            ["retriedCount"] = backlog.RetriedCount,
            ["atMaxAttemptsCount"] = backlog.MaxAttemptCount,
            ["exhaustedCount"] = backlog.ExhaustedCount,
            ["exhaustedLast24HoursCount"] = backlog.ExhaustedLast24HoursCount,
        };

        var problems = new List<string>();
        if (backlog.OldestPendingAgeSeconds > DegradedPendingAge.TotalSeconds)
            problems.Add("pending scans are older than the allowed age");
        if (backlog.StaleClaimCount > 0)
            problems.Add("scan claims have expired without completing");
        if (backlog.ExhaustedLast24HoursCount > 0)
            problems.Add("scans exhausted their retries in the last 24 hours");

        return problems.Count == 0
            ? HealthCheckResult.Healthy("File scan backlog is within limits.", data)
            : HealthCheckResult.Degraded(string.Join("; ", problems), data: data);
    }
}
