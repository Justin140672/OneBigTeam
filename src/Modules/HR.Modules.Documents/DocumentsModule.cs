using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Documents;

public static class DocumentsModule
{
    public static IServiceCollection AddDocumentsModule(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<DocumentsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "documents")));

        return services;
    }

    public static async Task MigrateDocumentsAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS documents");
        await db.Database.MigrateAsync();
    }
}
