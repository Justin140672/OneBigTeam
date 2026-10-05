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
            options.UseVersionedAggregates().UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "onboarding")));

        services.AddScoped<IIntegrationEventHandler<EmployeeCreatedIntegrationEvent>, EmployeeCreatedHandler>();
        services.AddScoped<ITaskCompletionAction, CompleteOnboardingTaskFromTaskAction>();
        services.AddScoped<GetOnboardingOverviewHandler>();
        services.AddScoped<GetOnboardingStatusHandler>();
        services.AddScoped<GetMyOnboardingStatusHandler>();
        services.AddScoped<IOnboardingStatusReader, OnboardingStatusReader>();
        services.AddScoped<IOnboardingReportReader, OnboardingReportReader>();
        services.AddScoped<GetTeamOnboardingHandler>();
        services.AddScoped<OnboardingResourceAuthorizer>();
        services.AddScoped<OnboardingReminderJob>();
        services.AddScoped<IOnboardingHistoryReplayer, OnboardingHistoryReplayer>();
        services.AddScoped<IWorkloadActionProvider, OutstandingOnboardingTasksWorkloadActionProvider>();
        services.AddScoped<IEmployeeRelatedTaskSourceProvider, OnboardingEmployeeTaskSourceProvider>();

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

    public static async Task SeedE2eOnboardingPlansAsync(
        this IServiceProvider services,
        IEnumerable<(Guid CompanyId, Guid EmployeeId, DateOnly StartDate, string EmployeeName)> employees)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OnboardingDbContext>();
        var taskCreator = scope.ServiceProvider.GetRequiredService<ITaskCreator>();
        var hrTaskCreator = scope.ServiceProvider.GetRequiredService<IHrTaskCreator>();
        var templateReader = scope.ServiceProvider.GetRequiredService<IOnboardingTemplateReader>();
        var now = DateTimeOffset.UtcNow;

        foreach (var (companyId, employeeId, startDate, employeeName) in employees)
        {
            if (await db.OnboardingPlans.AnyAsync(p => p.EmployeeId == employeeId))
                continue;

            var plan = Domain.OnboardingPlan.Create(Guid.NewGuid(), companyId, employeeId, startDate, notes: null, now);
            db.OnboardingPlans.Add(plan);

            var defaultTemplateId = await templateReader.GetDefaultOnboardingTemplateIdAsync(companyId, CancellationToken.None);
            var templateTasks = defaultTemplateId is { } templateId
                ? await templateReader.GetActiveTasksAsync(companyId, templateId, CancellationToken.None)
                : [];

            foreach (var task in templateTasks)
            {
                var title = $"{task.Title} — {employeeName}";
                var dueDate = startDate.AddDays(task.DueDaysAfterStart);

                var onboardingTask = Domain.OnboardingTask.Create(
                    Guid.NewGuid(), companyId, plan.Id, title, task.Description, task.AssignTo, dueDate, now);
                db.OnboardingTasks.Add(onboardingTask);

                if (task.AssignTo == OnboardingTemplateTaskAssignTo.Hr)
                {
                    await hrTaskCreator.CreateForHrAsync(
                        companyId, employeeId, title, task.Description, task.Priority,
                        TaskSource.Onboarding, TaskActionType.Complete, dueDate, onboardingTask.Id,
                        CancellationToken.None);
                    continue;
                }

                var assignedEmployeeId = task.AssignTo == OnboardingTemplateTaskAssignTo.NewHire
                    ? employeeId
                    : (Guid?)null;

                await taskCreator.CreateAsync(
                    companyId,
                    createdBy:          employeeId,
                    title:              title,
                    description:        task.Description,
                    priority:           task.Priority,
                    source:             TaskSource.Onboarding,
                    actionType:         TaskActionType.Complete,
                    dueDate:            dueDate,
                    assignedEmployeeId: assignedEmployeeId,
                    assignedUserId:     assignedEmployeeId,
                    sourceEntityId:     onboardingTask.Id,
                    CancellationToken.None);
            }
        }

        await db.SaveChangesAsync();
    }

    public static async Task MigrateOnboardingAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OnboardingDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS onboarding");
        await db.Database.MigrateAsync();
    }
}
