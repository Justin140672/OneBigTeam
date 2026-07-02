using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HR.Modules.Sickness.Persistence;

internal sealed class SicknessDbContextFactory : IDesignTimeDbContextFactory<SicknessDbContext>
{
    public SicknessDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("SICKNESS_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=hr;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<SicknessDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", "sickness"));

        return new SicknessDbContext(optionsBuilder.Options);
    }
}
