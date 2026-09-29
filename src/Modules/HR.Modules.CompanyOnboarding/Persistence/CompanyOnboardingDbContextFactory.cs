using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HR.Modules.CompanyOnboarding.Persistence;

internal sealed class CompanyOnboardingDbContextFactory : IDesignTimeDbContextFactory<CompanyOnboardingDbContext>
{
    public CompanyOnboardingDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("COMPANY_ONBOARDING_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=hr;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<CompanyOnboardingDbContext>();
        optionsBuilder.UseVersionedAggregates().UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", "company_onboarding"));

        return new CompanyOnboardingDbContext(optionsBuilder.Options);
    }
}
