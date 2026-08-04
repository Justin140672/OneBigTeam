using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HR.Modules.CompanyOnboarding.Persistence;

/// <summary>
/// Design-time factory used by EF Core tooling (dotnet ef migrations).
/// Set the COMPANY_ONBOARDING_CONNECTION_STRING environment variable or update the fallback
/// to point at your local Postgres instance before running migrations.
/// </summary>
internal sealed class CompanyOnboardingDbContextFactory : IDesignTimeDbContextFactory<CompanyOnboardingDbContext>
{
    public CompanyOnboardingDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("COMPANY_ONBOARDING_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=hr;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<CompanyOnboardingDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", "company_onboarding"));

        return new CompanyOnboardingDbContext(optionsBuilder.Options);
    }
}
