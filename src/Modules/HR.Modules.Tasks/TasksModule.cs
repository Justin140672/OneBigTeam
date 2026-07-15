using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Features.CandidateHired;
using HR.Modules.Tasks.Features.CompleteTask;
using HR.Modules.Tasks.Features.CompleteTask.Actions;
using HR.Modules.Tasks.Features.GetEmployeeTasks;
using HR.Modules.Tasks.Features.GetMyTasks;
using HR.Modules.Tasks.Features.GetTeamTasks;
using HR.Modules.Tasks.Features.GetTask;
using HR.Modules.Tasks.Features.GetOutstandingTaskCount;
using HR.Modules.Tasks.Features.GetUnassignedTasks;
using HR.Modules.Tasks.Features.LeaveRequested;
using HR.Modules.Tasks.Features.ReturnToWorkReviewRequired;
using HR.Modules.Tasks.Features.SicknessEvidenceOverdue;
using HR.Modules.Tasks.Features.SicknessEvidenceRequested;
using HR.Modules.Tasks.Features.ReassignTask;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
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
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "tasks")));

        return services;
    }

    private static void AddFeatureServices(IServiceCollection services)
    {
        services.AddScoped<ITaskCreator, TaskCreator>();
        services.AddScoped<ITaskCompleter, TaskCompleter>();
        services.AddScoped<ITaskCanceller, TaskCanceller>();
        services.AddScoped<IOpenTaskBySourceEntityReader, OpenTaskBySourceEntityReader>();
        services.AddScoped<TaskCompletionDispatcher>();
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
        services.AddScoped<GetTeamTasksHandler>();
        services.AddScoped<GetEmployeeTasksHandler>();
        services.AddScoped<ReassignTaskHandler>();
        services.AddScoped<CompleteTaskHandler>();

        services.AddHostedService<DueSoonNotifier>();
    }

    public static async Task MigrateTasksAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS tasks");
        await db.Database.MigrateAsync();
    }

    public static async Task SeedTasksAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();

        if (await db.TaskItems.AnyAsync())
            return;

        var now = DateTimeOffset.UtcNow;
        var companyId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var devUserId  = Guid.Parse("30000000-0000-0000-0000-000000000001"); // Sarah Chen — matches DevAuthHandler

        // Seeded employee IDs from EmployeesModule
        var empCtoId      = devUserId; // Sarah Chen
        var empSenDev1Id  = Guid.Parse("30000000-0000-0000-0000-000000000002"); // James Okafor
        var empDev1Id     = Guid.Parse("30000000-0000-0000-0000-000000000004"); // Tom Williams
        var empHrMgrId    = Guid.Parse("30000000-0000-0000-0000-000000000005"); // Laura Bennett
        var empAe2Id      = Guid.Parse("30000000-0000-0000-0000-000000000010"); // Carlos Rivera

        // Probation review task — links to the active seeded probation review (50000000-...-000000000100)
        var taskProbationReviewId = Guid.Parse("a0000000-0000-0000-0000-000000000005");

        // Generic, non-domain-specific tasks (previously TaskSource.Manual — that source was
        // removed entirely; TaskSource.Workflow is the closest generic bucket). Kept only
        // because specific E2E tests exercise mechanics that are source-agnostic (the generic
        // CompleteTaskPanel fallback, and task reassignment) and need a task that doesn't match
        // any of the specialised Source+ActionType panel combinations in TaskViewDialog.
        // Assigned to Sarah — used by GeneralTaskCompletionTests (task-view detail + generic
        // completion flow). Also referenced by NotificationsModule seed data by ID.
        var taskGenericReviewId = Guid.Parse("a0000000-0000-0000-0000-000000000027");
        var taskGenericSurveyId = Guid.Parse("a0000000-0000-0000-0000-000000000028");
        // Assigned to Laura — used by TaskReassignmentTests.
        var taskLauraGenericAgendaId = Guid.Parse("a0000000-0000-0000-0000-000000000029");

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

            // Generic (non-domain-specific) task assigned to Sarah — exercises the
            // CompleteTaskPanel fallback in TaskViewDialog. Used by
            // GeneralTaskCompletionTests.TaskView_ShowsCorrectDetailsForGeneralTask and
            // referenced by NotificationsModule seed data.
            Make(taskGenericReviewId,
                "Review Q2 performance reports",
                "Gather scores from all department heads and summarise findings.",
                TaskPriority.High, TaskSource.Workflow,
                new DateOnly(2026, 6, 30), empCtoId),

            // Generic (non-domain-specific) task assigned to Sarah — a second, independent one
            // so completing it in GeneralTaskCompletionTests.TaskView_CompleteTask_ChangesStatusToCompleted
            // doesn't conflict with the task above. Referenced by NotificationsModule seed data.
            Make(taskGenericSurveyId,
                "Analyse employee satisfaction survey results",
                "Review responses from the May survey and prepare a summary report for leadership.",
                TaskPriority.High, TaskSource.Workflow,
                new DateOnly(2026, 6, 10), empCtoId),

            // Generic (non-domain-specific) task assigned to Laura — used by
            // TaskReassignmentTests, which reassigns this one away from Laura to James.
            Make(taskLauraGenericAgendaId,
                "Prepare board meeting agenda",
                "Draft the Q3 board meeting agenda including financial review and product roadmap.",
                TaskPriority.High, TaskSource.Workflow,
                new DateOnly(2026, 6, 25), empHrMgrId));

        // Probation review task — linked to the active seeded review in ProbationModule seed.
        // Assigned to Sarah (dev user) so it appears in her task list during E2E tests.
        db.TaskItems.Add(TaskItem.Create(
            taskProbationReviewId, companyId, empCtoId,
            "Complete probation review — Carlos Rivera",
            "Probation manager check-in due 7 May 2026.",
            TaskPriority.High, TaskSource.Probation, TaskActionType.Review,
            new DateOnly(2026, 5, 7),
            assignedEmployeeId: empCtoId,
            assignedUserId: devUserId,
            now,
            sourceEntityId: Guid.Parse("50000000-0000-0000-0000-000000000100")));

        // Second, independent probation review task — linked to the second active seeded
        // review in ProbationModule seed (Sophie Laurent). Kept separate from the Carlos
        // Rivera task above so ProbationReviewFlowTests (which completes this review) does not
        // mutate the review that ProbationReviewTaskTests relies on staying open.
        // Assigned to Sarah (dev user) so it appears in her task list during E2E tests.
        db.TaskItems.Add(TaskItem.Create(
            Guid.Parse("a0000000-0000-0000-0000-000000000026"), companyId, empCtoId,
            "Complete probation review — Sophie Laurent",
            "Probation manager check-in due 7 May 2026.",
            TaskPriority.High, TaskSource.Probation, TaskActionType.Review,
            new DateOnly(2026, 5, 7),
            assignedEmployeeId: empCtoId,
            assignedUserId: devUserId,
            now,
            sourceEntityId: Guid.Parse("50000000-0000-0000-0000-000000000101")));

        // Asset acknowledgement tasks — linked to seeded AssetAssignments in AssetsModule seed.
        db.TaskItems.Add(TaskItem.Create(
            Guid.Parse("a0000000-0000-0000-0000-000000000020"), companyId, empCtoId,
            "Acknowledge receipt of asset",
            "Please acknowledge that you have received and accepted responsibility for the assigned asset.",
            TaskPriority.Medium, TaskSource.Asset, TaskActionType.Acknowledge,
            dueDate: new DateOnly(2026, 7, 7),
            assignedEmployeeId: empDev1Id,  // Tom Williams — MacBook Pro
            assignedUserId: empDev1Id,
            now,
            sourceEntityId: Guid.Parse("c0000000-0000-0000-0000-000000000003")));

        db.TaskItems.Add(TaskItem.Create(
            Guid.Parse("a0000000-0000-0000-0000-000000000021"), companyId, empCtoId,
            "Acknowledge receipt of asset",
            "Please acknowledge that you have received and accepted responsibility for the assigned asset.",
            TaskPriority.Medium, TaskSource.Asset, TaskActionType.Acknowledge,
            dueDate: new DateOnly(2026, 7, 7),
            assignedEmployeeId: empCtoId,   // Sarah Chen — Dell monitor
            assignedUserId: devUserId,
            now,
            sourceEntityId: Guid.Parse("c0000000-0000-0000-0000-000000000005")));

        // Asset acknowledgement task for Laura — linked to her seeded AssetAssignment in
        // AssetsModule seed (Dell Latitude laptop). Added additively alongside Sarah's and
        // Tom's equivalents above so E2E dashboard-widget scenarios can use Laura, whose "/"
        // dashboard remains reachable.
        db.TaskItems.Add(TaskItem.Create(
            Guid.Parse("a0000000-0000-0000-0000-000000000023"), companyId, empCtoId,
            "Acknowledge receipt of asset",
            "Please acknowledge that you have received and accepted responsibility for the assigned asset.",
            TaskPriority.Medium, TaskSource.Asset, TaskActionType.Acknowledge,
            dueDate: new DateOnly(2026, 7, 7),
            assignedEmployeeId: empHrMgrId,  // Laura Bennett — Dell Latitude laptop
            assignedUserId: empHrMgrId,
            now,
            sourceEntityId: Guid.Parse("c0000000-0000-0000-0000-000000000008")));

        // Asset return task for Sarah — linked to her Dell monitor assignment.
        db.TaskItems.Add(TaskItem.Create(
            Guid.Parse("a0000000-0000-0000-0000-000000000022"), companyId, empCtoId,
            "Return asset",
            "Please return the assigned asset.",
            TaskPriority.Medium, TaskSource.Asset, TaskActionType.Return,
            dueDate: new DateOnly(2026, 7, 14),
            assignedEmployeeId: empCtoId,   // Sarah Chen — Dell monitor
            assignedUserId: devUserId,
            now,
            sourceEntityId: Guid.Parse("c0000000-0000-0000-0000-000000000005")));

        // Document upload tasks — linked to seeded DocumentRequests in DocumentsModule seed.
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
