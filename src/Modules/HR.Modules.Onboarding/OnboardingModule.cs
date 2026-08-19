using HR.Modules.Tasks.Contracts;
using HR.Modules.Onboarding.Features.CompleteOnboardingTaskFromTask;
using HR.Modules.Onboarding.Features.CreateOnboardingPlanOnEmployeeCreated;
using HR.Modules.Onboarding.Features.GetOnboardingOverview;
using HR.Modules.Onboarding.Features.GetMyOnboardingStatus;
using HR.Modules.Onboarding.Features.GetOnboardingStatus;
using HR.Modules.Onboarding.Features.GetTeamOnboarding;
using HR.Modules.Onboarding.Jobs;
using HR.Modules.Onboarding.Persistence;
using HR.Modules.Onboarding.Services;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Onboarding;

public static class OnboardingModule
{
    public static IServiceCollection AddOnboardingModule(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<OnboardingDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "onboarding")));

        services.AddScoped<IIntegrationEventHandler<EmployeeCreatedIntegrationEvent>, EmployeeCreatedHandler>();
        services.AddScoped<ITaskCompletionAction, CompleteOnboardingTaskFromTaskAction>();
        services.AddScoped<GetOnboardingOverviewHandler>();
        services.AddScoped<GetOnboardingStatusHandler>();
        services.AddScoped<GetMyOnboardingStatusHandler>();
        services.AddScoped<IOnboardingStatusReader, OnboardingStatusReader>();
        services.AddScoped<IOnboardingReportReader, OnboardingReportReader>();
        services.AddScoped<GetTeamOnboardingHandler>();
        services.AddScoped<OnboardingReminderJob>();
        services.AddScoped<IOnboardingHistoryReplayer, OnboardingHistoryReplayer>();
        services.AddScoped<IWorkloadActionProvider, OutstandingOnboardingTasksWorkloadActionProvider>();

        return services;
    }

    public static WebApplication UseOnboardingRecurringJobs(this WebApplication app)
    {
        var jobManager = app.Services.GetRequiredService<IRecurringJobManager>();
        jobManager.AddOrUpdate<OnboardingReminderJob>(
            "onboarding-reminders",
            job => job.ExecuteAsync(),
            Cron.Daily(7));
        return app;
    }

    public static async Task MigrateOnboardingAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OnboardingDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS onboarding");
        await db.Database.MigrateAsync();
    }
}
