using HR.Modules.Assets.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Assets;

public static class AssetsModule
{
    public static IServiceCollection AddAssetsModule(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<AssetsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "assets")));

        return services;
    }

    public static async Task MigrateAssetsAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS assets");
        await db.Database.MigrateAsync();
    }

    public static Task SeedAssetsAsync(this IServiceProvider services)
    {
        return Task.CompletedTask;
    }
}
