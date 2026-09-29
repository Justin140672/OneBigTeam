using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HR.Modules.Companies.Persistence;

internal sealed class CompaniesDbContextFactory : IDesignTimeDbContextFactory<CompaniesDbContext>
{
    public CompaniesDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("COMPANIES_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=hr;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<CompaniesDbContext>();
        optionsBuilder.UseVersionedAggregates().UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", "companies"));

        return new CompaniesDbContext(optionsBuilder.Options);
    }
}
