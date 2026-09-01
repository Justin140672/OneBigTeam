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
        services.AddScoped<OnboardingResourceAuthorizer>();
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

    /// <summary>
    /// E2E-only: gives each supplied employee a NotStarted <see cref="Domain.OnboardingPlan"/>
    /// with the three default checklist tasks — exactly mirroring the fallback (no linked
    /// onboarding template) path of
    /// <see cref="Features.CreateOnboardingPlanOnEmployeeCreated.EmployeeCreatedHandler"/> for an
    /// employee created with no manager: same three OnboardingTask rows AND the three matching
    /// unassigned Tasks-module tasks (so they surface in the HR Inbox just as they would for a
    /// UI-created employee). Employees seeded directly by the Employees module bypass that handler
    /// entirely, so tests covering the employee Onboarding tab / onboarding-task completion need
    /// this. Idempotent per employee.
    /// </summary>
    public static async Task SeedE2eOnboardingPlansAsync(
        this IServiceProvider services,
        IEnumerable<(Guid CompanyId, Guid EmployeeId, DateOnly StartDate, string EmployeeName)> employees)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OnboardingDbContext>();
        var taskCreator = scope.ServiceProvider.GetRequiredService<ITaskCreator>();
        var now = DateTimeOffset.UtcNow;

        foreach (var (companyId, employeeId, startDate, employeeName) in employees)
        {
            if (await db.OnboardingPlans.AnyAsync(p => p.EmployeeId == employeeId))
                continue;

            var plan = Domain.OnboardingPlan.Create(Guid.NewGuid(), companyId, employeeId, startDate, notes: null, now);
            db.OnboardingPlans.Add(plan);

            (string Title, string Description, OnboardingTemplateTaskAssignTo AssignTo, DateOnly Due, TaskPriority Priority)[] defaults =
            {
                ($"Set up workstation and system access — {employeeName}",
                 $"Provision equipment, accounts and system access ahead of {employeeName}'s start date.",
                 OnboardingTemplateTaskAssignTo.Unassigned, startDate, TaskPriority.High),
                ($"Send welcome email and first-day details — {employeeName}",
                 $"Send {employeeName} their first-day joining instructions and welcome pack.",
                 OnboardingTemplateTaskAssignTo.Manager, startDate, TaskPriority.Medium),
                ($"Schedule welcome and induction meeting — {employeeName}",
                 $"Book an induction meeting with {employeeName} during their first week.",
                 OnboardingTemplateTaskAssignTo.Manager, startDate.AddDays(7), TaskPriority.Medium),
            };

            foreach (var d in defaults)
            {
                var onboardingTask = Domain.OnboardingTask.Create(
                    Guid.NewGuid(), companyId, plan.Id, d.Title, d.Description, d.AssignTo, d.Due, now);
                db.OnboardingTasks.Add(onboardingTask);

                // No-manager employee => every default checklist task is unassigned (lands in the
                // HR Inbox), matching EmployeeCreatedHandler's behaviour for a manager-less hire.
                await taskCreator.CreateAsync(
                    companyId,
                    createdBy:          employeeId,
                    title:              d.Title,
                    description:        d.Description,
                    priority:           d.Priority,
                    source:             TaskSource.Onboarding,
                    actionType:         TaskActionType.Complete,
                    dueDate:            d.Due,
                    assignedEmployeeId: null,
                    assignedUserId:     null,
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
