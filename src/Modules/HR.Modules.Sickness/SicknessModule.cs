using Hangfire;
using HR.Modules.Sickness.Features.CloseSicknessRecord;
using HR.Modules.Sickness.Features.FulfilEvidenceRequest;
using HR.Modules.Sickness.Features.CreateSicknessCategory;
using HR.Modules.Sickness.Features.DeactivateSicknessCategory;
using HR.Modules.Sickness.Features.GetSicknessRecord;
using HR.Modules.Sickness.Features.ListEmployeeSicknessRecords;
using HR.Modules.Sickness.Features.ListSicknessCategories;
using HR.Modules.Sickness.Features.RecordMySickness;
using HR.Modules.Sickness.Features.RecordSickness;
using HR.Modules.Sickness.Features.UpdateSicknessRecord;
using HR.Modules.Sickness.Features.UpdateSicknessCategory;
using HR.Modules.Sickness.Jobs;
using HR.Modules.Sickness.Persistence;
using HR.SharedKernel.Contracts;
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
        services.AddScoped<GetSicknessRecordHandler>();
        services.AddScoped<ListEmployeeSicknessRecordsHandler>();
        services.AddScoped<UpdateSicknessRecordHandler>();
        services.AddScoped<CloseSicknessRecordHandler>();
        services.AddScoped<FitNoteRequestJob>();
        services.AddScoped<ITaskCompletionAction, SicknessEvidenceUploadCompletionAction>();
    }

    public static WebApplication UseSicknessRecurringJobs(this WebApplication app)
    {
        var jobManager = app.Services.GetRequiredService<IRecurringJobManager>();
        jobManager.AddOrUpdate<FitNoteRequestJob>(
            "fit-note-requests",
            job => job.ExecuteAsync(),
            Cron.Daily(3));
        return app;
    }

    public static async Task MigrateSicknessAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SicknessDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS sickness");
        await db.Database.MigrateAsync();
    }
}
