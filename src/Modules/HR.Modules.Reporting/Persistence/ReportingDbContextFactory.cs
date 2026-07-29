using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HR.Modules.Reporting.Persistence;

internal sealed class ReportingDbContextFactory : IDesignTimeDbContextFactory<ReportingDbContext>
{
    public ReportingDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("REPORTING_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=hr;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<ReportingDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", "reporting"));

        return new ReportingDbContext(optionsBuilder.Options);
    }
}
