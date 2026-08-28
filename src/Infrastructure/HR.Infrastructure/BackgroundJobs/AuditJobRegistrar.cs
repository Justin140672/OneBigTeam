using Hangfire;

namespace HR.Infrastructure.BackgroundJobs;

/// <summary>
/// AUD-01: registers the audit pending-item promotion job to run every minute.
/// Implements <see cref="IRecurringJobRegistrar"/> so Infrastructure registers itself
/// without any module needing to know about the job.
/// </summary>
internal sealed class AuditJobRegistrar : IRecurringJobRegistrar
{
    public void Register(IRecurringJobManager manager)
    {
        manager.AddOrUpdate<AuditPendingItemPromotionJob>(
            "audit-pending-promotion",
            job => job.ExecuteAsync(CancellationToken.None),
            Cron.Minutely());
    }
}
