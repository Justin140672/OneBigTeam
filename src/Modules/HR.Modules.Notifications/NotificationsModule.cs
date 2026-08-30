using HR.Modules.Employees.Contracts;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Notifications.Domain;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.Modules.Notifications.Features.AcknowledgeAdministrativeAlert;
using HR.Modules.Notifications.Features.GetAdministrativeAlerts;
using HR.Modules.Notifications.Features.GetAdministrativeAlertUnreadCount;
using HR.Modules.Notifications.Features.GetMyNotifications;
using HR.Modules.Notifications.Features.GetUnreadNotificationCount;
using HR.Modules.Notifications.Features.MarkAdministrativeAlertRead;
using HR.Modules.Notifications.Features.ResolveAdministrativeAlert;
using HR.Modules.Notifications.Features.MarkAllNotificationsRead;
using HR.Modules.Notifications.Features.MarkNotificationRead;
using HR.Modules.Notifications.Features.NotifyOnCandidateHired;
using HR.Modules.Notifications.Features.NotifyOnEmployeeCreated;
using HR.Modules.Notifications.Features.NotifyOnLeaveRequested;
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

        // ADM-03: administrative alerts / incidents inbox.
        services.AddScoped<IAdministrativeAlertWriter, AdministrativeAlertWriter>();
        services.AddScoped<GetAdministrativeAlertsHandler>();
        services.AddScoped<GetAdministrativeAlertUnreadCountHandler>();
        services.AddScoped<MarkAdministrativeAlertReadHandler>();
        services.AddScoped<AcknowledgeAdministrativeAlertHandler>();
        services.AddScoped<ResolveAdministrativeAlertHandler>();

        // NOT-07: event-driven notification consumers. Notifications is a pure consumer of
        // integration events published by their owning modules (Leave, Employees, Recruitment) —
        // it never references those modules' implementation projects, only their sanctioned
        // *.Contracts surfaces (IManagerReader, IEmployeeNameReader, IPositionProfileReader) plus
        // the shared IHrAdministratorDirectory already used elsewhere (Probation, Offboarding,
        // Support) for HR-queue resolution.
        services.AddScoped<IIntegrationEventHandler<LeaveRequestedIntegrationEvent>, NotifyOnLeaveRequestedHandler>();
        services.AddScoped<IIntegrationEventHandler<EmployeeCreatedIntegrationEvent>, NotifyOnEmployeeCreatedHandler>();
        services.AddScoped<IIntegrationEventHandler<CandidateHiredIntegrationEvent>, NotifyOnCandidateHiredHandler>();

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

        // Fixed task IDs matching TasksModule seed data. These used to reference four
        // TaskSource.Manual tasks (a0000000-...0001/0002/0003/0004); that source has been
        // removed entirely, along with its seeded tasks. The two generic Workflow-sourced
        // tasks that replaced the Q2-review/survey tasks (still assigned to Sarah) are
        // referenced here instead, so Sarah keeps at least one valid, non-dangling
        // notification (see IndividualNotificationTests, which requires this).
        var taskGenericReviewId = Guid.Parse("a0000000-0000-0000-0000-000000000027");
        var taskGenericSurveyId = Guid.Parse("a0000000-0000-0000-0000-000000000028");

        db.Notifications.AddRange(
            Notification.Create(Guid.NewGuid(), companyId, empCtoId,
                "New task assigned: Review Q2 performance reports",
                "Gather scores from all department heads and summarise findings.",
                taskGenericReviewId, now.AddHours(-2),
                NotificationType.TaskAssigned, NotificationPriority.High),

            Notification.Create(Guid.NewGuid(), companyId, empCtoId,
                "Overdue: Analyse employee satisfaction survey results",
                "This task was due on 10 Jun 2026 and has not been completed.",
                taskGenericSurveyId, now.AddMinutes(-15),
                NotificationType.TaskOverdue, NotificationPriority.Urgent));

        await db.SaveChangesAsync();
    }
}
