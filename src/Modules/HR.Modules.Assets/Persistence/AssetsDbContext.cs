using HR.Modules.Assets.Domain;
using HR.SharedKernel.Idempotency;
using HR.SharedKernel.Outbox;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Assets.Persistence;

internal sealed class AssetsDbContext : DbContext
{
    public AssetsDbContext(DbContextOptions<AssetsDbContext> options)
        : base(options)
    {
    }

    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetAssignment> AssetAssignments => Set<AssetAssignment>();
    public DbSet<AssetCategory> AssetCategories => Set<AssetCategory>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<AuditOutboxEntry> AuditOutboxEntries => Set<AuditOutboxEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("assets");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AssetsDbContext).Assembly);
        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration<IdempotencyRecord>());
        modelBuilder.ApplyConfiguration(new AuditOutboxEntryConfiguration<AuditOutboxEntry>());
    }
}
