using Microsoft.EntityFrameworkCore;

namespace HR.Infrastructure.Persistence;

internal sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    /// <summary>AUD-01: durable staging table — promoted to AuditEvents by the background job.</summary>
    public DbSet<AuditPendingItem> AuditPendingItems => Set<AuditPendingItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("audit");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuditDbContext).Assembly);
    }

    /// <summary>
    /// AUD-02: committed audit records (<see cref="AuditEvent"/>) are append-only. Any attempt to
    /// update or delete an existing <see cref="AuditEvent"/> row through this context is rejected —
    /// corrective information must be represented by a new audit event, not by mutating history.
    /// <see cref="AuditPendingItem"/> is a mutable staging table by design (the promotion job
    /// transitions its Status), so it is deliberately not covered by the guard.
    /// </summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceAppendOnly();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnforceAppendOnly()
    {
        foreach (var entry in ChangeTracker.Entries<AuditEvent>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"AUD-02: audit records are immutable. " +
                    $"Attempted {entry.State} on {entry.Entity.GetType().Name}. " +
                    $"Corrective information must be recorded as a new audit event.");
            }
        }
    }
}

