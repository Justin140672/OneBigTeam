using Hangfire;

namespace HR.Infrastructure.BackgroundJobs;

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
