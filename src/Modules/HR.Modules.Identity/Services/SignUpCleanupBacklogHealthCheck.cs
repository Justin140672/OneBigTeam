using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HR.Modules.Identity.Services;

internal sealed class SignUpCleanupBacklogHealthCheck(IdentityDbContext db, IClock clock) : IHealthCheck
{
    internal static readonly TimeSpan WarningBeforeRetention = TimeSpan.FromDays(2);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNowOffset();
        var warnCutoff = now - (SignUpOperation.Retention - WarningBeforeRetention);

        var unswept = db.SignUpOperations.AsNoTracking()
            .Where(o => o.Status == SignUpOperation.StatusFailed && o.SweptAt == null);
        var unsweptCount = await unswept.CountAsync(cancellationToken);
        var nearingDeadline = await unswept.CountAsync(o => o.CompletedAt < warnCutoff, cancellationToken);

        var data = new Dictionary<string, object>
        {
            ["unsweptFailureCount"] = unsweptCount,
            ["unsweptFailuresNearingRetentionCount"] = nearingDeadline,
        };

        return nearingDeadline == 0
            ? HealthCheckResult.Healthy("Signup cleanup backlog is within limits.", data)
            : HealthCheckResult.Degraded(
                "Failed signup operations are approaching their retention deadline without a successful late-resource sweep.",
                data: data);
    }
}
