using FluentValidation;
using Hangfire;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Features.CandidateHired;
using HR.Modules.Tasks.Features.CompleteTask;
using HR.Modules.Tasks.Features.CompleteTask.Actions;
using HR.Modules.Tasks.Features.GetEmployeeTasks;
using HR.Modules.Tasks.Features.GetMyTasks;
using HR.Modules.Tasks.Features.GetTask;
using HR.Modules.Tasks.Features.GetOutstandingTaskCount;
using HR.Modules.Tasks.Features.GetUnassignedTasks;
using HR.Modules.Tasks.Features.LeaveRequested;
using HR.Modules.Tasks.Features.ReturnToWorkReviewRequired;
using HR.Modules.Tasks.Features.SicknessEvidenceOverdue;
using HR.Modules.Tasks.Features.SicknessEvidenceRequested;
using HR.Modules.Tasks.Features.ReassignTask;
using HR.Modules.Tasks.Features.ResetProgrammaticTaskCompletion;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Tasks;

public static class TasksModule
{
    public static IServiceCollection AddTasksModule(
        this IServiceCollection services,
        string connectionString)
    {
        AddFeatureServices(services);

        services.AddDbContext<TasksDbContext>(options =>
            options.UseVersionedAggregates().UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "tasks")));

        return services;
    }

    private static void AddFeatureServices(IServiceCollection services)
    {
        services.AddScoped<ITaskCreator, TaskCreator>();
        services.AddScoped<TaskCompleter>();
        services.AddScoped<ITaskCompleter>(sp => sp.GetRequiredService<TaskCompleter>());
        services.AddScoped<TaskCanceller>();
        services.AddScoped<ITaskCanceller>(sp => sp.GetRequiredService<TaskCanceller>());
        services.AddScoped<ITaskResolution, TaskResolution>();
        services.AddScoped<TaskCompletionAuditDelivery>();
        services.AddScoped<TaskRecoveryAuditDelivery>();
        services.AddScoped<Jobs.TaskRecoveryAuditDeliveryJob>();
        services.AddScoped<ITaskCompletionOperationStateReader, TaskCompletionOperationStateReader>();
        services.AddScoped<ITaskCompletionRecovery, TaskCompletionRecovery>();
        services.AddScoped<ResetProgrammaticTaskCompletionHandler>();
        services.AddScoped<TaskCompletionAdjudicator>();
        services.AddScoped<Features.AdjudicateTaskCompletionOperation.AdjudicateTaskCompletionOperationHandler>();
        services.AddScoped<IValidator<Features.AdjudicateTaskCompletionOperation.AdjudicateTaskCompletionOperationRequest>, Features.AdjudicateTaskCompletionOperation.AdjudicateTaskCompletionOperationValidator>();
        services.AddScoped<Features.ListTaskCompletionOperationsRequiringIntervention.ListTaskCompletionOperationsRequiringInterventionHandler>();
        services.AddScoped<IValidator<Features.ListTaskCompletionOperationsRequiringIntervention.ListTaskCompletionOperationsRequiringInterventionRequest>, Features.ListTaskCompletionOperationsRequiringIntervention.ListTaskCompletionOperationsRequiringInterventionValidator>();
        services.AddScoped<IValidator<ResetProgrammaticTaskCompletionRequest>, ResetProgrammaticTaskCompletionValidator>();
        services.AddScoped<Jobs.ProgrammaticTaskCompletionReconciliationJob>();
        services.AddScoped<ITaskRescheduler, TaskRescheduler>();
        services.AddScoped<ITaskReassigner, TaskReassigner>();
        services.AddScoped<IOpenTaskBySourceEntityReader, OpenTaskBySourceEntityReader>();
        services.AddScoped<ITaskLinkBySourceEntityReader, TaskLinkBySourceEntityReader>();
        services.AddScoped<IWorkloadActionProvider, EmployeeTasksOverdueWorkloadActionProvider>();
        services.AddScoped<IWorkloadActionProvider, ManagerTasksOverdueWorkloadActionProvider>();
        services.AddScoped<TaskCompletionDispatcher>();
        // Ticket 4 (P1): retries CompleteTaskHandler's notification/audit confirmation for a
        // completion whose business action already succeeded — see Jobs/TaskCompletionEffectsJob.cs.
        services.AddScoped<Jobs.TaskCompletionEffectsJob>();
        services.AddScoped<TasksResourceAuthorizer>();
        services.AddScoped<ITaskCompletionAction, ProbationTaskCompletionAction>();
        services.AddScoped<ITaskCompletionAction, LeaveTaskCompletionAction>();
        services.AddScoped<ITaskCompletionAction, AssetTaskCompletionAction>();
        services.AddScoped<ITaskCompletionAction, AssetReturnTaskCompletionAction>();
        services.AddScoped<ITaskCompletionAction, InterviewFeedbackTaskCompletionAction>();
        services.AddScoped<IIntegrationEventHandler<LeaveRequestedIntegrationEvent>, LeaveRequestedHandler>();
        services.AddScoped<IIntegrationEventHandler<SicknessEvidenceRequestedIntegrationEvent>, SicknessEvidenceRequestedHandler>();
        services.AddScoped<IIntegrationEventHandler<SicknessEvidenceRequestedIntegrationEvent>, NotifyHrOfFitNoteThresholdHandler>();
        services.AddScoped<IIntegrationEventHandler<SicknessEvidenceOverdueIntegrationEvent>, NotifyHrOfOverdueFitNoteHandler>();
        services.AddScoped<IIntegrationEventHandler<ReturnToWorkReviewRequiredIntegrationEvent>, ReturnToWorkReviewRequiredHandler>();
        services.AddScoped<IIntegrationEventHandler<CandidateHiredIntegrationEvent>, NotifyHrOfCandidateHiredHandler>();

        services.AddScoped<GetTaskHandler>();
        services.AddScoped<GetUnassignedTasksHandler>();
        services.AddScoped<GetOutstandingTaskCountHandler>();
        services.AddScoped<GetMyTasksHandler>();
        services.AddScoped<GetEmployeeTasksHandler>();
        services.AddScoped<ReassignTaskHandler>();
        services.AddScoped<CompleteTaskHandler>();

        services.AddHostedService<DueSoonNotifier>();
        services.AddScoped<Jobs.IdempotencyMaintenanceJob>();
        services.AddScoped<Jobs.TaskCompletionReconciliationJob>();
    }

    public static WebApplication UseTasksRecurringJobs(this WebApplication app)
    {
        var jobManager = app.Services.GetRequiredService<IRecurringJobManager>();
        // Ticket 3 (P1) follow-up item 4: clean up expired idempotency records.
        jobManager.AddOrUpdate<Jobs.IdempotencyMaintenanceJob>(
            "tasks-idempotency-maintenance",
            job => job.ExecuteAsync(),
            "*/5 * * * *");
        // Ticket 11 (P1): repairs TaskCompletionOperation rows abandoned mid-completion (stale
        // Pending, abandoned DispatchApplied) — see TaskCompletionReconciliationJob for the recovery
        // scenarios it guards against.
        jobManager.AddOrUpdate<Jobs.TaskCompletionReconciliationJob>(
            "tasks-completion-reconciliation",
            job => job.ExecuteAsync(),
            "*/10 * * * *");
        jobManager.AddOrUpdate<Jobs.TaskRecoveryAuditDeliveryJob>(
            "tasks-recovery-audit-delivery",
            job => job.ExecuteAsync(),
            "*/2 * * * *");
        jobManager.AddOrUpdate<Jobs.ProgrammaticTaskCompletionReconciliationJob>(
            "tasks-programmatic-completion-reconciliation",
            job => job.ExecuteAsync(),
            "*/5 * * * *");
        return app;
    }

    public static async Task MigrateTasksAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS tasks");
        await db.Database.MigrateAsync();
    }


    public static async Task SeedE2eProbationReviewTaskAsync(this IServiceProvider services, Guid companyId, Guid managerId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        var taskId = Guid.Parse("a0000000-0000-0000-0000-0000000000f5");
        if (await db.TaskItems.AnyAsync(t => t.Id == taskId))
            return;

        db.TaskItems.Add(TaskItem.Create(
            taskId, companyId, managerId,
            "Complete probation review — E2E SeedProbationTask",
            "Probation manager check-in due 7 May 2026.",
            TaskPriority.High, TaskSource.Probation, TaskActionType.Review,
            new DateOnly(2026, 5, 7),
            assignedEmployeeId: managerId,
            assignedUserId: managerId,
            DateTimeOffset.UtcNow,
            sourceEntityId: Guid.Parse("50000000-0000-0000-0000-000000000105")));

        await db.SaveChangesAsync();
    }
    public static async Task SeedTasksAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        if (await db.TaskItems.AnyAsync())
            return;

        var now = DateTimeOffset.UtcNow;
        var companyId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var devUserId  = Guid.Parse("30000000-0000-0000-0000-000000000001");

        var empCtoId      = devUserId;
        var empSenDev1Id  = Guid.Parse("30000000-0000-0000-0000-000000000002");
        var empDev1Id     = Guid.Parse("30000000-0000-0000-0000-000000000004");
        var empHrMgrId    = Guid.Parse("30000000-0000-0000-0000-000000000005");
        var empSalesMgrId = Guid.Parse("30000000-0000-0000-0000-000000000008");
        var empAe2Id      = Guid.Parse("30000000-0000-0000-0000-000000000010");

        var taskProbationReviewId = Guid.Parse("a0000000-0000-0000-0000-000000000005");

        var taskGenericReviewId = Guid.Parse("a0000000-0000-0000-0000-000000000027");
        var taskGenericSurveyId = Guid.Parse("a0000000-0000-0000-0000-000000000028");
        var taskLauraGenericAgendaId = Guid.Parse("a0000000-0000-0000-0000-000000000029");
        var taskDavidGenericPipelineId = Guid.Parse("a0000000-0000-0000-0000-00000000002a");

        TaskItem Make(
            Guid id,
            string title, string? description,
            TaskPriority priority, TaskSource source,
            DateOnly? dueDate, Guid? assignedEmployeeId,
            TaskItemStatus status = TaskItemStatus.Open,
            TaskActionType actionType = TaskActionType.Complete)
        {
            var t = TaskItem.Create(
                id, companyId, devUserId,
                title, description, priority, source, actionType,
                dueDate, assignedEmployeeId, assignedEmployeeId, now);
            if (status == TaskItemStatus.InProgress) t.Start(now);
            return t;
        }

        db.TaskItems.AddRange(
            Make(Guid.NewGuid(),
                "Schedule probation review — Tom Williams",
                "Tom's 3-month probation ends 20 May. Book a review meeting with his line manager.",
                TaskPriority.Medium, TaskSource.Probation,
                new DateOnly(2026, 6, 20), empDev1Id, TaskItemStatus.InProgress),

            Make(Guid.NewGuid(),
                "Update annual leave policy documentation",
                null,
                TaskPriority.Low, TaskSource.Leave,
                new DateOnly(2026, 7, 15), empHrMgrId),

            Make(Guid.NewGuid(),
                "Complete compliance training sign-off",
                "Confirm all Engineering staff have completed the data protection module.",
                TaskPriority.Critical, TaskSource.Compliance,
                new DateOnly(2026, 6, 17), empSenDev1Id),

            Make(Guid.NewGuid(),
                "Send onboarding pack — Carlos Rivera",
                null,
                TaskPriority.Medium, TaskSource.Onboarding,
                new DateOnly(2026, 6, 18), empAe2Id),

            Make(Guid.NewGuid(),
                "Collect signed contract amendments",
                "Three employees accepted revised terms. Collect signed copies and file.",
                TaskPriority.High, TaskSource.Document,
                null, null),

            Make(taskGenericReviewId,
                "Review Q2 performance reports",
                "Gather scores from all department heads and summarise findings.",
                TaskPriority.High, TaskSource.Workflow,
                new DateOnly(2026, 6, 30), empCtoId),

            Make(taskGenericSurveyId,
                "Analyse employee satisfaction survey results",
                "Review responses from the May survey and prepare a summary report for leadership.",
                TaskPriority.High, TaskSource.Workflow,
                new DateOnly(2026, 6, 10), empCtoId),

            Make(taskLauraGenericAgendaId,
                "Prepare board meeting agenda",
                "Draft the Q3 board meeting agenda including financial review and product roadmap.",
                TaskPriority.High, TaskSource.Workflow,
                new DateOnly(2026, 6, 25), empHrMgrId),

            Make(taskDavidGenericPipelineId,
                "Review Q3 sales pipeline",
                "Summarise pipeline health and forecast for the quarterly leadership review.",
                TaskPriority.Medium, TaskSource.Workflow,
                new DateOnly(2026, 6, 28), empSalesMgrId));

        db.TaskItems.Add(TaskItem.Create(
            taskProbationReviewId, companyId, empCtoId,
            "Complete probation review — Carlos Rivera",
            "Probation manager check-in due 7 May 2026.",
            TaskPriority.High, TaskSource.Probation, TaskActionType.Review,
            new DateOnly(2026, 5, 7),
            assignedEmployeeId: empSalesMgrId,
            assignedUserId: empSalesMgrId,
            now,
            sourceEntityId: Guid.Parse("50000000-0000-0000-0000-000000000100")));

        db.TaskItems.Add(TaskItem.Create(
            Guid.Parse("a0000000-0000-0000-0000-000000000026"), companyId, empCtoId,
            "Complete probation review — Sophie Laurent",
            "Probation manager check-in due 7 May 2026.",
            TaskPriority.High, TaskSource.Probation, TaskActionType.Review,
            new DateOnly(2026, 5, 7),
            assignedEmployeeId: empSalesMgrId,
            assignedUserId: empSalesMgrId,
            now,
            sourceEntityId: Guid.Parse("50000000-0000-0000-0000-000000000101")));

        // Third, independent probation review task — links to the third active seeded review in
        // ProbationModule seed (Emma Jones, a pending FinalDecision review, not ManagerCheckIn).
        // Ticket 18: assigned to Laura Bennett (HrAdministrator) so the same persona used by
        // ProbationRecordAdministrativeEditTests can, from a second browser tab, independently
        // complete this FinalDecision review (Pass/Fail) — transitioning Emma's probation record
        // to a terminal status — while that suite's first tab has the record open for an
        // "administrative correction" edit. Kept separate from the Carlos/Sophie tasks above so
        // completing it doesn't interfere with ProbationReviewTaskTests/ProbationReviewFlowTests.
        db.TaskItems.Add(TaskItem.Create(
            Guid.Parse("a0000000-0000-0000-0000-00000000002b"), companyId, empCtoId,
            "Complete probation review — Emma Jones",
            "Probation final decision due 7 July 2026.",
            TaskPriority.High, TaskSource.Probation, TaskActionType.Review,
            new DateOnly(2026, 7, 7),
            assignedEmployeeId: empHrMgrId,
            assignedUserId: empHrMgrId,
            now,
            sourceEntityId: Guid.Parse("50000000-0000-0000-0000-000000000102")));

        db.TaskItems.Add(TaskItem.Create(
            Guid.Parse("a0000000-0000-0000-0000-00000000002c"), companyId, empCtoId,
            "Complete probation review — Marcus Diallo",
            "Probation final decision due 7 July 2026.",
            TaskPriority.High, TaskSource.Probation, TaskActionType.Review,
            new DateOnly(2026, 7, 7),
            assignedEmployeeId: empHrMgrId,
            assignedUserId: empHrMgrId,
            now,
            sourceEntityId: Guid.Parse("50000000-0000-0000-0000-000000000104")));

        db.TaskItems.Add(TaskItem.Create(
            Guid.Parse("a0000000-0000-0000-0000-000000000020"), companyId, empCtoId,
            "Acknowledge receipt of asset",
            "Please acknowledge that you have received and accepted responsibility for the assigned asset.",
            TaskPriority.Medium, TaskSource.Asset, TaskActionType.Acknowledge,
            dueDate: new DateOnly(2026, 7, 7),
            assignedEmployeeId: empDev1Id,
            assignedUserId: empDev1Id,
            now,
            sourceEntityId: Guid.Parse("c0000000-0000-0000-0000-000000000003")));

        db.TaskItems.Add(TaskItem.Create(
            Guid.Parse("a0000000-0000-0000-0000-000000000021"), companyId, empCtoId,
            "Acknowledge receipt of asset",
            "Please acknowledge that you have received and accepted responsibility for the assigned asset.",
            TaskPriority.Medium, TaskSource.Asset, TaskActionType.Acknowledge,
            dueDate: new DateOnly(2026, 7, 7),
            assignedEmployeeId: empCtoId,
            assignedUserId: devUserId,
            now,
            sourceEntityId: Guid.Parse("c0000000-0000-0000-0000-000000000005")));

        db.TaskItems.Add(TaskItem.Create(
            Guid.Parse("a0000000-0000-0000-0000-000000000023"), companyId, empCtoId,
            "Acknowledge receipt of asset",
            "Please acknowledge that you have received and accepted responsibility for the assigned asset.",
            TaskPriority.Medium, TaskSource.Asset, TaskActionType.Acknowledge,
            dueDate: new DateOnly(2026, 7, 7),
            assignedEmployeeId: empHrMgrId,
            assignedUserId: empHrMgrId,
            now,
            sourceEntityId: Guid.Parse("c0000000-0000-0000-0000-000000000008")));

        db.TaskItems.Add(TaskItem.Create(
            Guid.Parse("a0000000-0000-0000-0000-000000000022"), companyId, empCtoId,
            "Return asset",
            "Please return the assigned asset.",
            TaskPriority.Medium, TaskSource.Asset, TaskActionType.Return,
            dueDate: new DateOnly(2026, 7, 14),
            assignedEmployeeId: empCtoId,
            assignedUserId: devUserId,
            now,
            sourceEntityId: Guid.Parse("c0000000-0000-0000-0000-000000000005")));

        db.TaskItems.AddRange(
            TaskItem.Create(
                Guid.Parse("a0000000-0000-0000-0000-000000000013"), companyId, empSenDev1Id,
                "Upload Passport",
                "Please upload a copy of your Passport.",
                TaskPriority.Medium, TaskSource.Document, TaskActionType.Upload,
                dueDate: null,
                assignedEmployeeId: empSenDev1Id,
                assignedUserId: empSenDev1Id,
                now,
                sourceEntityId: Guid.Parse("b0000000-0000-0000-0000-000000000004")),

            TaskItem.Create(
                Guid.Parse("a0000000-0000-0000-0000-000000000014"), companyId, empSenDev1Id,
                "Upload Right To Work",
                "Please upload a copy of your Right To Work.",
                TaskPriority.Medium, TaskSource.Document, TaskActionType.Upload,
                dueDate: null,
                assignedEmployeeId: empSenDev1Id,
                assignedUserId: empSenDev1Id,
                now,
                sourceEntityId: Guid.Parse("b0000000-0000-0000-0000-000000000005")),

            TaskItem.Create(
                Guid.Parse("a0000000-0000-0000-0000-000000000010"), companyId, empDev1Id,
                "Upload Passport",
                "Please upload a copy of your Passport.",
                TaskPriority.Medium, TaskSource.Document, TaskActionType.Upload,
                dueDate: null,
                assignedEmployeeId: empDev1Id,
                assignedUserId: empDev1Id,
                now,
                sourceEntityId: Guid.Parse("b0000000-0000-0000-0000-000000000001")),

            TaskItem.Create(
                Guid.Parse("a0000000-0000-0000-0000-000000000011"), companyId, empAe2Id,
                "Upload Passport",
                "Please upload a copy of your Passport.",
                TaskPriority.Medium, TaskSource.Document, TaskActionType.Upload,
                dueDate: null,
                assignedEmployeeId: empAe2Id,
                assignedUserId: empAe2Id,
                now,
                sourceEntityId: Guid.Parse("b0000000-0000-0000-0000-000000000002")),

            TaskItem.Create(
                Guid.Parse("a0000000-0000-0000-0000-000000000012"), companyId, empAe2Id,
                "Upload Right To Work",
                "Please upload a copy of your Right To Work.",
                TaskPriority.Medium, TaskSource.Document, TaskActionType.Upload,
                dueDate: null,
                assignedEmployeeId: empAe2Id,
                assignedUserId: empAe2Id,
                now,
                sourceEntityId: Guid.Parse("b0000000-0000-0000-0000-000000000003")));

        await db.SaveChangesAsync();
    }
}
