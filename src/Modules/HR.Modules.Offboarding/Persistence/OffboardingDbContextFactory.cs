using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HR.Modules.Offboarding.Persistence;

internal sealed class OffboardingDbContextFactory : IDesignTimeDbContextFactory<OffboardingDbContext>
{
    public OffboardingDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("OFFBOARDING_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=hr;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<OffboardingDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", "offboarding"));

        return new OffboardingDbContext(optionsBuilder.Options);
    }
}
