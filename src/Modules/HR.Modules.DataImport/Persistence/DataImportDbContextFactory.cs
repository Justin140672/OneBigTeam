using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HR.Modules.DataImport.Persistence;

internal sealed class DataImportDbContextFactory : IDesignTimeDbContextFactory<DataImportDbContext>
{
    public DataImportDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("DATAIMPORT_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=hr;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<DataImportDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", "data_import"));

        return new DataImportDbContext(optionsBuilder.Options);
    }
}
