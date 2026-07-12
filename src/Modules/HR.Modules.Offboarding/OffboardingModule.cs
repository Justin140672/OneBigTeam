using FluentValidation;
using Hangfire;
using HR.Modules.Offboarding.Features.CompleteOffboardingTaskFromTask;
using HR.Modules.Offboarding.Features.GetOffboardingOverview;
using HR.Modules.Offboarding.Features.GetOffboardingStatus;
using HR.Modules.Offboarding.Features.StartOffboarding;
using HR.Modules.Offboarding.Jobs;
using HR.Modules.Offboarding.Persistence;
using HR.Modules.Offboarding.Services;
using HR.Infrastructure.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Offboarding;

public static class OffboardingModule
{
    public static IServiceCollection AddOffboardingModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<OffboardingDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "offboarding")));

        services.AddScoped<ITaskCompletionAction, CompleteOffboardingTaskFromTaskAction>();
        services.AddScoped<StartOffboardingHandler>();
        services.AddScoped<GetOffboardingOverviewHandler>();
        services.AddScoped<GetOffboardingStatusHandler>();
        services.AddScoped<IOffboardingStatusReader, OffboardingStatusReader>();
        services.AddScoped<IValidator<StartOffboardingRequest>, StartOffboardingValidator>();
        services.AddScoped<OffboardingReminderJob>();

        return services;
    }

    public static WebApplication UseOffboardingRecurringJobs(this WebApplication app)
    {
        var jobManager = app.Services.GetRequiredService<IRecurringJobManager>();
        jobManager.AddOrUpdate<OffboardingReminderJob>(
            "offboarding-reminders",
            job => job.ExecuteAsync(),
            Cron.Daily(8));
        return app;
    }

    public static async Task MigrateOffboardingAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OffboardingDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS offboarding");
        await db.Database.MigrateAsync();
    }
}
