using FluentValidation;
using HR.Modules.Probation.Features.CreateProbationRecord;
using HR.Modules.Probation.Features.CreateProbationReview;
using HR.Modules.Probation.Features.GetProbationRecord;
using HR.Modules.Probation.Features.UpdateProbationRecord;
using HR.Modules.Probation.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Probation;

public static class ProbationModule
{
    public static IServiceCollection AddProbationModule(
        this IServiceCollection services,
        string connectionString)
    {
        AddFeatureServices(services);

        services.AddDbContext<ProbationDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "probation")));

        return services;
    }

    private static void AddFeatureServices(IServiceCollection services)
    {
        services.AddScoped<CreateProbationRecordHandler>();
        services.AddScoped<IValidator<CreateProbationRecordRequest>, CreateProbationRecordValidator>();
        services.AddScoped<GetProbationRecordHandler>();
        services.AddScoped<UpdateProbationRecordHandler>();
        services.AddScoped<IValidator<UpdateProbationRecordRequest>, UpdateProbationRecordValidator>();
        services.AddScoped<CreateProbationReviewHandler>();
        services.AddScoped<IValidator<CreateProbationReviewRequest>, CreateProbationReviewValidator>();
    }

    public static async Task MigrateProbationAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProbationDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS probation");
        await db.Database.MigrateAsync();
    }

    public static async Task SeedProbationAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProbationDbContext>();

        if (await db.ProbationRecords.AnyAsync())
            return;
    }
}
