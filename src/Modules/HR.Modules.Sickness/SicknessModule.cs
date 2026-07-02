using HR.Modules.Sickness.Features.CreateSicknessCategory;
using HR.Modules.Sickness.Features.DeactivateSicknessCategory;
using HR.Modules.Sickness.Features.ListSicknessCategories;
using HR.Modules.Sickness.Features.RecordMySickness;
using HR.Modules.Sickness.Features.RecordSickness;
using HR.Modules.Sickness.Features.UpdateSicknessCategory;
using HR.Modules.Sickness.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Sickness;

public static class SicknessModule
{
    public static IServiceCollection AddSicknessModule(
        this IServiceCollection services,
        string connectionString)
    {
        AddFeatureServices(services);

        services.AddDbContext<SicknessDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "sickness")));

        return services;
    }

    private static void AddFeatureServices(IServiceCollection services)
    {
        services.AddScoped<ListSicknessCategoriesHandler>();
        services.AddScoped<CreateSicknessCategoryHandler>();
        services.AddScoped<UpdateSicknessCategoryHandler>();
        services.AddScoped<DeactivateSicknessCategoryHandler>();
        services.AddScoped<RecordSicknessHandler>();
        services.AddScoped<RecordMySicknessHandler>();
    }

    public static async Task MigrateSicknessAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SicknessDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS sickness");
        await db.Database.MigrateAsync();
    }
}
