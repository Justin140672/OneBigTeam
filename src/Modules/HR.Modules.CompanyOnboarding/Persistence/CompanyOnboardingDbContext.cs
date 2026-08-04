using HR.Modules.CompanyOnboarding.Domain;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.CompanyOnboarding.Persistence;

internal sealed class CompanyOnboardingDbContext : DbContext
{
    public CompanyOnboardingDbContext(DbContextOptions<CompanyOnboardingDbContext> options)
        : base(options)
    {
    }

    public DbSet<CompanyOnboardingProgress> Progress => Set<CompanyOnboardingProgress>();
    public DbSet<CompanyOnboardingTaskCompletion> TaskCompletions => Set<CompanyOnboardingTaskCompletion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("company_onboarding");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CompanyOnboardingDbContext).Assembly);
    }
}
