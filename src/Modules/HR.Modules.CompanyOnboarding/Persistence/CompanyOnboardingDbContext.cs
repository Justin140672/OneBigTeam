using HR.Modules.CompanyOnboarding.Domain;
using HR.SharedKernel.Idempotency;
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
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("company_onboarding");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CompanyOnboardingDbContext).Assembly);
        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration<IdempotencyRecord>());
    }
}
