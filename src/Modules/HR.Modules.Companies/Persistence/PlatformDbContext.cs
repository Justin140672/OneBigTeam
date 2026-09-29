using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence.Configurations.Platform;
using HR.SharedKernel.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Persistence;

internal sealed class PlatformDbContext : DbContext
{
    public PlatformDbContext(DbContextOptions<PlatformDbContext> options)
        : base(options)
    {
    }

    public DbSet<CustomerDatabaseAssignment> CustomerDatabaseAssignments => Set<CustomerDatabaseAssignment>();
    public DbSet<PlatformSettings> PlatformSettings => Set<PlatformSettings>();
    public DbSet<PlatformMetricsSnapshot> PlatformMetricsSnapshots => Set<PlatformMetricsSnapshot>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("platform");

        modelBuilder.ApplyConfiguration(new CustomerDatabaseAssignmentConfiguration());
        modelBuilder.ApplyConfiguration(new PlatformSettingsConfiguration());
        modelBuilder.ApplyConfiguration(new PlatformMetricsSnapshotConfiguration());
        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration<IdempotencyRecord>());
    }
}
