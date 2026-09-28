using HR.Modules.Companies.Domain;
using HR.SharedKernel.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Persistence;

/// <summary>
/// DbContext for platform-wide entities that are not tenant-scoped (do not have company_id).
/// Owns the "platform" schema, separate from the "companies" schema owned by CompaniesDbContext.
///
/// Platform entities include:
/// - CustomerDatabaseAssignments: tracks per-customer database assignments within the shared platform
/// - PlatformSettings: singleton platform configuration (trial length, pricing, feature flags, etc.)
/// - PlatformMetricsSnapshots: append-only platform-wide metrics snapshots
///
/// This context is intended for admin-writable and system-computed data that applies across all tenants.
/// </summary>
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
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlatformDbContext).Assembly);
        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration<IdempotencyRecord>());
    }
}
