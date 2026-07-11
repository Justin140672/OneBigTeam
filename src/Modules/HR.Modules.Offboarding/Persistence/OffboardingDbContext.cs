using HR.Modules.Offboarding.Domain;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("offboarding");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OffboardingDbContext).Assembly);
    }
}
