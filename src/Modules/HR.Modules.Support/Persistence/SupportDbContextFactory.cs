using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HR.Modules.Support.Persistence;

internal sealed class SupportDbContextFactory : IDesignTimeDbContextFactory<SupportDbContext>
{
    public SupportDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("SUPPORT_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=hr;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<SupportDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", "support"));

        return new SupportDbContext(optionsBuilder.Options);
    }
}
