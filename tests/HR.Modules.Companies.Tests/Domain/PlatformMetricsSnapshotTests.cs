using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Tests.Domain;

public class PlatformMetricsSnapshotTests
{
    [Fact]
    public void Create_Sets_All_Properties()
    {
        var id = Guid.NewGuid();
        var snapshotDate = new DateOnly(2026, 8, 1);
        var computedAt = new DateTimeOffset(2026, 8, 1, 3, 0, 0, TimeSpan.Zero);

        var snapshot = PlatformMetricsSnapshot.Create(
            id,
            snapshotDate,
            computedAt,
            activeCompanies: 12,
            activeUsers: 34,
            storageConsumedBytes: 987654321L,
            backgroundJobsSucceededTotal: 56);

        Assert.Equal(id, snapshot.Id);
        Assert.Equal(snapshotDate, snapshot.SnapshotDate);
        Assert.Equal(computedAt, snapshot.ComputedAt);
        Assert.Equal(12, snapshot.ActiveCompanies);
        Assert.Equal(34, snapshot.ActiveUsers);
        Assert.Equal(987654321L, snapshot.StorageConsumedBytes);
        Assert.Equal(56, snapshot.BackgroundJobsSucceededTotal);
    }
}
