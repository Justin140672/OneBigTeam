using HR.Modules.Employees.Contracts;
using HR.Modules.Companies.Contracts;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Notifications.Domain;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.Modules.Notifications.Features.GetMyNotifications;
using HR.Modules.Notifications.Features.GetOperationalAlert;
using HR.Modules.Notifications.Features.ListOperationalAlerts;
using HR.Modules.Notifications.Features.PreviewProductUpdateRecipients;
using HR.Modules.Notifications.Features.ResolveOperationalAlert;
using HR.Modules.Notifications.Features.SendProductUpdate;
using HR.Modules.Notifications.Features.GetUnreadNotificationCount;
using HR.Modules.Notifications.Features.MarkAllNotificationsRead;
using HR.Modules.Notifications.Features.MarkNotificationRead;
using HR.Modules.Notifications.Features.NotifyOnCandidateHired;
using HR.Modules.Notifications.Features.NotifyOnEmployeeCreated;
using HR.Modules.Notifications.Features.NotifyOnLeaveRequested;
using HR.Modules.Notifications.Features.NotifyOnOrganisationDataExportCompleted;
using HR.Modules.Notifications.Jobs;
using HR.Modules.Notifications.Persistence;
using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Notifications;

public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(
        this IServiceCollection services,
        string connectionString,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        services.AddDbContext<NotificationsDbContext>(options =>
            options.UseVersionedAggregates().UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "notifications")));

        services.Configure<OperationalAlertEmailOptions>(configuration.GetSection("OperationalAlerts"));

        services.AddScoped<INotificationWriter, NotificationWriter>();
        services.AddScoped<GetMyNotificationsHandler>();
        services.AddScoped<GetUnreadNotificationCountHandler>();
        services.AddScoped<MarkNotificationReadHandler>();
        services.AddScoped<MarkAllNotificationsReadHandler>();

        services.AddScoped<IAdministrativeAlertWriter, AdministrativeAlertWriter>();

        services.AddScoped<ListOperationalAlertsHandler>();
        services.AddScoped<GetOperationalAlertHandler>();
        services.AddScoped<ResolveOperationalAlertHandler>();
        services.AddScoped<ListOperationalAlertsValidator>();
        services.AddScoped<GetOperationalAlertValidator>();
        services.AddScoped<ResolveOperationalAlertValidator>();

        services.AddScoped<PreviewProductUpdateRecipientsHandler>();
        services.AddScoped<SendProductUpdateHandler>();
        services.AddScoped<SendProductUpdateValidator>();

        services.AddScoped<SendOperationalAlertEmailJob>();

        services.AddScoped<ReconcileStalledOperationalAlertEmailDeliveriesJob>();

        services.AddScoped<IIntegrationEventHandler<LeaveRequestedIntegrationEvent>, NotifyOnLeaveRequestedHandler>();
        services.AddScoped<IIntegrationEventHandler<EmployeeCreatedIntegrationEvent>, NotifyOnEmployeeCreatedHandler>();
        services.AddScoped<IIntegrationEventHandler<CandidateHiredIntegrationEvent>, NotifyOnCandidateHiredHandler>();

        services.AddScoped<IIntegrationEventHandler<OrganisationDataExportCompletedIntegrationEvent>, NotifyOnOrganisationDataExportCompletedHandler>();

        services.AddScoped<PurgeExpiredReadNotificationsJob>();

        services.AddScoped<ReconcilePendingEmailDeliveriesJob>();
        services.AddScoped<ReconcileMissingNotificationAuditsJob>();
        services.AddScoped<Jobs.IdempotencyMaintenanceJob>();

        return services;
    }

    public static WebApplication UseNotificationsRecurringJobs(this WebApplication app)
    {
        var jobManager = app.Services.GetRequiredService<IRecurringJobManager>();
        jobManager.AddOrUpdate<PurgeExpiredReadNotificationsJob>(
            "notifications-retention-sweep",
            job => job.ExecuteAsync(CancellationToken.None),
            Cron.Daily(3));

        jobManager.AddOrUpdate<ReconcilePendingEmailDeliveriesJob>(
            "notifications-reconcile-pending-email-deliveries",
            job => job.ExecuteAsync(CancellationToken.None),
            Cron.Hourly());
        jobManager.AddOrUpdate<ReconcileMissingNotificationAuditsJob>(
            "notifications-reconcile-missing-creation-audits",
            job => job.ExecuteAsync(CancellationToken.None),
            Cron.Hourly());

        jobManager.AddOrUpdate<ReconcileStalledOperationalAlertEmailDeliveriesJob>(
            "notifications-reconcile-stalled-operational-alert-emails",
            job => job.ExecuteAsync(CancellationToken.None),
            Cron.Hourly());

        // Ticket 3 (P1) follow-up item 4: clean up expired idempotency records.
        jobManager.AddOrUpdate<Jobs.IdempotencyMaintenanceJob>(
            "notifications-idempotency-maintenance",
            job => job.ExecuteAsync(),
            "*/5 * * * *");

        return app;
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
        var empCtoId   = Guid.Parse("30000000-0000-0000-0000-000000000001");

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

    public static async Task SeedE2eOperationalAlertsAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        var resolvedId = Guid.Parse("0000e2ea-0000-0000-0000-0000000000f0");
        if (await db.AdministrativeAlerts.AnyAsync(a => a.Id == resolvedId))
            return;

        var companyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var now = DateTimeOffset.UtcNow;

        static RaiseAdministrativeAlertCommand Cmd(
            Guid companyId,
            string dedupKey,
            AdministrativeAlertCategory category,
            DateTimeOffset occurredAt) =>
            new(
                companyId,
                AdministrativeAlertSeverity.Warning,
                category,
                "E2E seeded operational alert",
                "E2E seeded detail — a background job failed and needs operator attention.",
                occurredAt,
                dedupKey,
                "OrganisationDataExport",
                Guid.Parse("0000e2ea-0000-0000-0000-00000000abcd"),
                "Investigate the failing job and re-run it.",
                null,
                2);

        for (var i = 1; i <= 8; i++)
        {
            var id = Guid.Parse($"0000e2ea-0000-0000-0000-0000000000{i:D2}");
            db.AdministrativeAlerts.Add(AdministrativeAlert.Raise(
                id, Cmd(companyId, $"e2e-op-alert-report-{i:D2}", AdministrativeAlertCategory.ReportGeneration, now.AddHours(-i)), now.AddHours(-i)));
        }

        db.AdministrativeAlerts.Add(AdministrativeAlert.Raise(
            Guid.Parse("0000e2ea-0000-0000-0000-00000000c001"),
            Cmd(companyId, "e2e-op-alert-compliance-01", AdministrativeAlertCategory.Compliance, now.AddHours(-9)),
            now.AddHours(-9)));

        var resolved = AdministrativeAlert.Raise(
            resolvedId,
            Cmd(companyId, "e2e-op-alert-resolved-01", AdministrativeAlertCategory.ReportGeneration, now.AddHours(-48)),
            now.AddHours(-48));
        resolved.Resolve(Guid.Parse("00000000-0000-0000-0000-0000000000aa"), "E2E: resolved during seed.", now.AddHours(-47));
        db.AdministrativeAlerts.Add(resolved);

        await db.SaveChangesAsync();
    }
}
