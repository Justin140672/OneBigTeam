using FluentValidation;
using HR.Modules.Assets.Features.CreateAssetCategory;
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
        AddFeatureServices(services);

        services.AddDbContext<AssetsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "assets")));

        return services;
    }

    private static void AddFeatureServices(IServiceCollection services)
    {
        services.AddScoped<CreateAssetCategoryHandler>();
        services.AddScoped<IValidator<CreateAssetCategoryRequest>, CreateAssetCategoryValidator>();
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
