using HR.Modules.Tasks.Contracts;
using FluentValidation;
using Hangfire;
using HR.Modules.Employees.Contracts;
using HR.Modules.Offboarding.Features.CancelOffboardingOnLeavingProcessCancelled;
using HR.Modules.Offboarding.Features.CompleteOffboardingTaskFromTask;
using HR.Modules.Offboarding.Features.GetOffboardingOverview;
using HR.Modules.Offboarding.Features.GetOffboardingStatus;
using HR.Modules.Offboarding.Features.RescheduleOffboardingOnLeavingDateChanged;
using HR.Modules.Offboarding.Features.StartOffboarding;
using HR.Modules.Offboarding.Jobs;
using HR.Modules.Offboarding.Persistence;
using HR.Modules.Offboarding.Services;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
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
        services.AddScoped<IOffboardingDetailReader, OffboardingDetailReader>();
        services.AddScoped<IOffboardingReportReader, OffboardingReportReader>();
        services.AddScoped<IOffboardingPlanCoordinator, OffboardingPlanCoordinator>();
        services.AddScoped<IOffboardingHistoryReplayer, OffboardingHistoryReplayer>();
        services.AddScoped<IValidator<StartOffboardingRequest>, StartOffboardingValidator>();
        services.AddScoped<OffboardingReminderJob>();
        services.AddScoped<IWorkloadActionProvider, OutstandingOffboardingTasksWorkloadActionProvider>();

        // OFF-03: creates the Tasks-module TaskItem for OffboardingTasks that don't have one yet —
        // used by StartOffboardingHandler right after the plan/tasks are committed, and again by
        // OffboardingPlanCreationReconciliationJob to retry anything that failed the first time.
        services.AddScoped<OffboardingTaskSynchronizer>();

        // OFF-01: second consumer of EmployeeLeavingProcessCancelledIntegrationEvent (alongside
        // the Leave module's), plus the daily reconciliation job that catches up on anything this
        // consumer (or Employees' own direct, synchronous call) failed to fully synchronise with
        // the Tasks module.
        services.AddScoped<IIntegrationEventHandler<EmployeeLeavingProcessCancelledIntegrationEvent>, CancelOffboardingOnLeavingProcessCancelledHandler>();
        services.AddScoped<OffboardingCancellationReconciliationJob>();

        // OFF-03: daily reconciliation for (a) leaving processes with no active offboarding plan
        // (the automatic trigger from Employees' StartLeavingProcessHandler failed or was lost) and
        // (b) offboarding plans whose OffboardingTasks are missing their corresponding Tasks-module
        // TaskItems (a partial failure between the durable Offboarding write and the cross-module
        // sync).
        services.AddScoped<OffboardingPlanCreationReconciliationJob>();

        // OFF-02: consumer of EmployeeLeavingDateSetIntegrationEvent (published on both leaving
        // process start and amendment) — keeps the active plan's LastWorkingDay and outstanding
        // task due dates aligned whenever HR changes the employee's leaving date/last working day.
        services.AddScoped<IIntegrationEventHandler<EmployeeLeavingDateSetIntegrationEvent>, RescheduleOffboardingOnLeavingDateChangedHandler>();

        return services;
    }

    public static WebApplication UseOffboardingRecurringJobs(this WebApplication app)
    {
        var jobManager = app.Services.GetRequiredService<IRecurringJobManager>();
        jobManager.AddOrUpdate<OffboardingReminderJob>(
            "offboarding-reminders",
            job => job.ExecuteAsync(),
            Cron.Daily(8));
        jobManager.AddOrUpdate<OffboardingCancellationReconciliationJob>(
            "offboarding-cancellation-reconciliation",
            job => job.ExecuteAsync(),
            Cron.Daily(9));
        jobManager.AddOrUpdate<OffboardingPlanCreationReconciliationJob>(
            "offboarding-plan-creation-reconciliation",
            job => job.ExecuteAsync(),
            Cron.Daily(10));
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
