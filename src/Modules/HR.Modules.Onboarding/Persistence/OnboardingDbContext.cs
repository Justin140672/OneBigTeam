using HR.Modules.Onboarding.Domain;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Onboarding.Persistence;

internal sealed class OnboardingDbContext : DbContext
{
    public OnboardingDbContext(DbContextOptions<OnboardingDbContext> options)
        : base(options)
    {
    }

    public DbSet<OnboardingPlan> OnboardingPlans => Set<OnboardingPlan>();
    public DbSet<OnboardingTaskTemplate> OnboardingTaskTemplates => Set<OnboardingTaskTemplate>();
    public DbSet<OnboardingTask> OnboardingTasks => Set<OnboardingTask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("onboarding");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OnboardingDbContext).Assembly);
    }
}
