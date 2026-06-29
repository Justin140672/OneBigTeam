using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Features.CompleteTask;
using HR.Modules.Tasks.Features.CompleteTask.Actions;
using HR.Modules.Tasks.Features.GetEmployeeTasks;
using HR.Modules.Tasks.Features.GetMyTasks;
using HR.Modules.Tasks.Features.GetTeamTasks;
using HR.Modules.Tasks.Features.GetTask;
using HR.Modules.Tasks.Features.GetUnassignedTasks;
using HR.Modules.Tasks.Features.LeaveRequested;
using HR.Modules.Tasks.Features.ReassignTask;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.SharedKernel;
using HR.SharedKernel.Contracts;
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
        services.AddScoped<TaskCompletionDispatcher>();
        services.AddScoped<ITaskCompletionAction, ProbationTaskCompletionAction>();
        services.AddScoped<ITaskCompletionAction, LeaveTaskCompletionAction>();
        services.AddScoped<IIntegrationEventHandler<LeaveRequestedIntegrationEvent>, LeaveRequestedHandler>();

        services.AddScoped<GetTaskHandler>();
        services.AddScoped<GetUnassignedTasksHandler>();
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

        // Fixed IDs for tasks assigned to Sarah so notifications can reference them
        var taskQ2ReviewId      = Guid.Parse("a0000000-0000-0000-0000-000000000001");
        var taskBoardAgendaId   = Guid.Parse("a0000000-0000-0000-0000-000000000002");
        var taskInterviewId     = Guid.Parse("a0000000-0000-0000-0000-000000000003");
        var taskSurveyId        = Guid.Parse("a0000000-0000-0000-0000-000000000004");
        // Probation review task — links to the active seeded probation review (50000000-...-000000000100)
        var taskProbationReviewId = Guid.Parse("a0000000-0000-0000-0000-000000000005");

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
            Make(taskQ2ReviewId,
                "Review Q2 performance reports",
                "Gather scores from all department heads and summarise findings.",
                TaskPriority.High, TaskSource.Manual,
                new DateOnly(2026, 6, 30), empCtoId),

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

            Make(taskBoardAgendaId,
                "Prepare board meeting agenda",
                "Draft the Q3 board meeting agenda including financial review and product roadmap.",
                TaskPriority.High, TaskSource.Manual,
                new DateOnly(2026, 6, 25), empCtoId),

            Make(taskInterviewId,
                "Engineering lead interview debrief",
                "Consolidate panel feedback and make hiring recommendation to the board.",
                TaskPriority.Medium, TaskSource.Manual,
                new DateOnly(2026, 6, 22), empCtoId, TaskItemStatus.InProgress),

            Make(taskSurveyId,
                "Analyse employee satisfaction survey results",
                "Review responses from the May survey and prepare a summary report for leadership.",
                TaskPriority.High, TaskSource.Manual,
                new DateOnly(2026, 6, 10), empCtoId));

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

        await db.SaveChangesAsync();
    }
}
