namespace HR.Modules.Companies.Domain;

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
