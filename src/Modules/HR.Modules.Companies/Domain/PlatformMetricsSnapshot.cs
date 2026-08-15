namespace HR.Modules.Companies.Domain;

/// <summary>
/// Point-in-time platform-wide snapshot of metrics that have no other historical record (active
/// companies, active users, storage consumed, cumulative background jobs succeeded), computed at
/// most once per calendar day (idempotent per SnapshotDate) each time an admin views the Application
/// Metrics dashboard. Accumulates as an append-only history starting from when this feature shipped
/// — there is no attempt to backfill history predating it. Unlike CustomerBillingSnapshot this is
/// NOT company-scoped; it is a platform-wide system aggregate, analogous to a global/system table
/// under the database standards' tenant-isolation exception.
/// </summary>
internal sealed class PlatformMetricsSnapshot
{
    private PlatformMetricsSnapshot() { }

    public Guid Id { get; private set; }
    public DateOnly SnapshotDate { get; private set; }
    public DateTimeOffset ComputedAt { get; private set; }
    public int ActiveCompanies { get; private set; }
    public int ActiveUsers { get; private set; }
    public long StorageConsumedBytes { get; private set; }
    public int BackgroundJobsSucceededTotal { get; private set; }

    public static PlatformMetricsSnapshot Create(
        Guid id,
        DateOnly snapshotDate,
        DateTimeOffset computedAt,
        int activeCompanies,
        int activeUsers,
        long storageConsumedBytes,
        int backgroundJobsSucceededTotal)
    {
        return new PlatformMetricsSnapshot
        {
            Id = id,
            SnapshotDate = snapshotDate,
            ComputedAt = computedAt,
            ActiveCompanies = activeCompanies,
            ActiveUsers = activeUsers,
            StorageConsumedBytes = storageConsumedBytes,
            BackgroundJobsSucceededTotal = backgroundJobsSucceededTotal,
        };
    }
}
