using HR.Modules.Notifications.Domain;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.Modules.Notifications.Features.GetMyNotifications;
using HR.Modules.Notifications.Features.GetUnreadNotificationCount;
using HR.Modules.Notifications.Features.MarkAllNotificationsRead;
using HR.Modules.Notifications.Features.MarkNotificationRead;
using HR.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Notifications;

public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<NotificationsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "notifications")));

        services.AddScoped<INotificationWriter, NotificationWriter>();
        services.AddScoped<GetMyNotificationsHandler>();
        services.AddScoped<GetUnreadNotificationCountHandler>();
        services.AddScoped<MarkNotificationReadHandler>();
        services.AddScoped<MarkAllNotificationsReadHandler>();

        return services;
    }

    public static async Task MigrateNotificationsAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS notifications");
        await db.Database.MigrateAsync();
    }

    public static async Task SeedNotificationsAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        if (await db.Notifications.AnyAsync())
            return;

        var now        = DateTimeOffset.UtcNow;
        var companyId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var empCtoId   = Guid.Parse("30000000-0000-0000-0000-000000000001"); // Sarah Chen

        // Fixed task IDs matching TasksModule seed data
        var taskQ2ReviewId    = Guid.Parse("a0000000-0000-0000-0000-000000000001");
        var taskBoardAgendaId = Guid.Parse("a0000000-0000-0000-0000-000000000002");
        var taskInterviewId   = Guid.Parse("a0000000-0000-0000-0000-000000000003");
        var taskSurveyId      = Guid.Parse("a0000000-0000-0000-0000-000000000004");

        db.Notifications.AddRange(
            Notification.Create(Guid.NewGuid(), companyId, empCtoId,
                "New task assigned: Review Q2 performance reports",
                "Gather scores from all department heads and summarise findings.",
                taskQ2ReviewId, now.AddHours(-2),
                NotificationType.TaskAssigned, NotificationPriority.High),

            Notification.Create(Guid.NewGuid(), companyId, empCtoId,
                "New task assigned: Prepare board meeting agenda",
                "Draft the Q3 board meeting agenda including financial review and product roadmap.",
                taskBoardAgendaId, now.AddHours(-1),
                NotificationType.TaskAssigned, NotificationPriority.High),

            Notification.Create(Guid.NewGuid(), companyId, empCtoId,
                "New task assigned: Engineering lead interview debrief",
                "Consolidate panel feedback and make hiring recommendation to the board.",
                taskInterviewId, now.AddDays(-1),
                NotificationType.TaskAssigned, NotificationPriority.Normal),

            Notification.Create(Guid.NewGuid(), companyId, empCtoId,
                "Due soon: Engineering lead interview debrief",
                "This task is due on 22 Jun 2026.",
                taskInterviewId, now.AddMinutes(-30),
                NotificationType.TaskDueSoon, NotificationPriority.High),

            Notification.Create(Guid.NewGuid(), companyId, empCtoId,
                "Overdue: Analyse employee satisfaction survey results",
                "This task was due on 10 Jun 2026 and has not been completed.",
                taskSurveyId, now.AddMinutes(-15),
                NotificationType.TaskOverdue, NotificationPriority.Urgent));

        await db.SaveChangesAsync();
    }
}
