using HR.Modules.Offboarding.Domain;
using HR.SharedKernel.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Offboarding.Persistence;

internal sealed class OffboardingDbContext : DbContext
{
    public OffboardingDbContext(DbContextOptions<OffboardingDbContext> options)
        : base(options)
    {
    }

    public DbSet<OffboardingPlan> OffboardingPlans => Set<OffboardingPlan>();
    public DbSet<OffboardingTask> OffboardingTasks => Set<OffboardingTask>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("offboarding");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OffboardingDbContext).Assembly);
        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration<IdempotencyRecord>());
    }
}
