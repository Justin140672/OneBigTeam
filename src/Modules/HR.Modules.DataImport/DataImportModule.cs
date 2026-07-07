using HR.Modules.DataImport.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.DataImport;

public static class DataImportModule
{
    public static IServiceCollection AddDataImportModule(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<DataImportDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "data_import")));

        return services;
    }

    public static async Task MigrateDataImportAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataImportDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS data_import");
        await db.Database.MigrateAsync();
    }
}
