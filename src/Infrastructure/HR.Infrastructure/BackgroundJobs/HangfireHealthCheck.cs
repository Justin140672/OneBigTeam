using Hangfire;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HR.Infrastructure.BackgroundJobs;

internal sealed class HangfireHealthCheck(JobStorage jobStorage) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var api = jobStorage.GetMonitoringApi();
            var servers = api.Servers();
            var stats = api.GetStatistics();

            if (servers.Count == 0)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "No Hangfire servers are running — background jobs cannot be processed.",
                    data: BuildData(servers.Count, stats)));
            }

            if (stats.Failed > 0)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"{stats.Failed} failed background job(s) are awaiting review.",
                    data: BuildData(servers.Count, stats)));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                $"Hangfire is healthy. {servers.Count} server(s) running.",
                data: BuildData(servers.Count, stats)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Hangfire storage could not be reached.", ex));
        }
    }

    private static IReadOnlyDictionary<string, object> BuildData(int serverCount, Hangfire.Storage.Monitoring.StatisticsDto stats) =>
        new Dictionary<string, object>
        {
            ["servers"] = serverCount,
            ["enqueued"] = stats.Enqueued,
            ["processing"] = stats.Processing,
            ["scheduled"] = stats.Scheduled,
            ["failed"] = stats.Failed,
            ["succeeded"] = stats.Succeeded,
            ["recurring"] = stats.Recurring,
        };
}
