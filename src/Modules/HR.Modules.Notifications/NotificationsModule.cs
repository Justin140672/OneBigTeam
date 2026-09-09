using HR.Modules.Employees.Contracts;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Notifications.Domain;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.Modules.Notifications.Features.GetMyNotifications;
using HR.Modules.Notifications.Features.GetOperationalAlert;
using HR.Modules.Notifications.Features.ListOperationalAlerts;
using HR.Modules.Notifications.Features.ResolveOperationalAlert;
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

        // Follow-up C: internal operations notification config for missing-file export alerts.
        services.Configure<OperationalAlertEmailOptions>(configuration.GetSection("OperationalAlerts"));

        services.AddScoped<INotificationWriter, NotificationWriter>();
        services.AddScoped<GetMyNotificationsHandler>();
        services.AddScoped<GetUnreadNotificationCountHandler>();
        services.AddScoped<MarkNotificationReadHandler>();
        services.AddScoped<MarkAllNotificationsReadHandler>();

        // ADM-03: administrative alert writer retained as an internal-only operational trail
        // (failure detection for background jobs, export auditing, authorization anomalies).
        // The customer-facing inbox / acknowledge / resolve / mark-read surface was removed.
        services.AddScoped<IAdministrativeAlertWriter, AdministrativeAlertWriter>();

        // Follow-up B: platform-admin Operational Alerts surface (list / details / resolve).
        services.AddScoped<ListOperationalAlertsHandler>();
        services.AddScoped<GetOperationalAlertHandler>();
        services.AddScoped<ResolveOperationalAlertHandler>();
        services.AddScoped<ListOperationalAlertsValidator>();
        services.AddScoped<GetOperationalAlertValidator>();
        services.AddScoped<ResolveOperationalAlertValidator>();

        // Follow-up C: one-off internal-operations notification email when a new missing-file
        // organisation-data-export alert opens.
        services.AddScoped<SendOperationalAlertEmailJob>();

        // Follow-up E: bounded reconciliation for operational-alert email deliveries that were saved
        // but never queued, or whose owning worker crashed mid-send.
        services.AddScoped<ReconcileStalledOperationalAlertEmailDeliveriesJob>();

        // NOT-07: event-driven notification consumers. Notifications is a pure consumer of
        // integration events published by their owning modules (Leave, Employees, Recruitment) —
        // it never references those modules' implementation projects, only their sanctioned
        // *.Contracts surfaces (IManagerReader, IEmployeeNameReader, IPositionProfileReader) plus
        // the shared IHrAdministratorDirectory already used elsewhere (Probation, Offboarding,
        // Support) for HR-queue resolution.
        services.AddScoped<IIntegrationEventHandler<LeaveRequestedIntegrationEvent>, NotifyOnLeaveRequestedHandler>();
        services.AddScoped<IIntegrationEventHandler<EmployeeCreatedIntegrationEvent>, NotifyOnEmployeeCreatedHandler>();
        services.AddScoped<IIntegrationEventHandler<CandidateHiredIntegrationEvent>, NotifyOnCandidateHiredHandler>();

        // Story 2: "your organisation data export is ready" notification for the requesting
        // company administrator. Published by the Reporting build job via Abstractions.
        services.AddScoped<IIntegrationEventHandler<OrganisationDataExportCompletedIntegrationEvent>, NotifyOnOrganisationDataExportCompletedHandler>();

        // NFR-07: scheduled read-notification retention sweep (dry-run by default).
        services.AddScoped<PurgeExpiredReadNotificationsJob>();

        // OBT-REM-12: bounded reconciliation for lost downstream work (email enqueue / creation
        // audit) after a partial failure in NotificationWriter.
        services.AddScoped<ReconcilePendingEmailDeliveriesJob>();
        services.AddScoped<ReconcileMissingNotificationAuditsJob>();

        return services;
    }

    /// <summary>
    /// NFR-07: registers the daily read-notification retention sweep. Runs in dry-run mode (logs +
    /// audits, deletes nothing) unless <c>Notifications:Retention:Enabled=true</c>.
    /// </summary>
    public static WebApplication UseNotificationsRecurringJobs(this WebApplication app)
    {
        var jobManager = app.Services.GetRequiredService<IRecurringJobManager>();
        jobManager.AddOrUpdate<PurgeExpiredReadNotificationsJob>(
            "notifications-retention-sweep",
            job => job.ExecuteAsync(CancellationToken.None),
            Cron.Daily(3));

        // OBT-REM-12: hourly bounded reconciliation sweeps — frequent enough to recover promptly,
        // cheap enough (grace period + per-company cap) to run every hour indefinitely.
        jobManager.AddOrUpdate<ReconcilePendingEmailDeliveriesJob>(
            "notifications-reconcile-pending-email-deliveries",
            job => job.ExecuteAsync(CancellationToken.None),
            Cron.Hourly());
        jobManager.AddOrUpdate<ReconcileMissingNotificationAuditsJob>(
            "notifications-reconcile-missing-creation-audits",
            job => job.ExecuteAsync(CancellationToken.None),
            Cron.Hourly());

        // Follow-up E: hourly bounded sweep recovering operational-alert email deliveries stranded
        // between "row saved" and "send completed".
        jobManager.AddOrUpdate<ReconcileStalledOperationalAlertEmailDeliveriesJob>(
            "notifications-reconcile-stalled-operational-alert-emails",
            job => job.ExecuteAsync(CancellationToken.None),
            Cron.Hourly());

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

    /// <summary>
    /// E2E-only (never integration DB / real environments — gated on <c>E2E_TESTING=true</c> in
    /// Program.cs, same pattern as the Employees E2E arrange-data pool). Operational alerts are
    /// system-generated and have no create UI, so the Playwright coverage for HR.Admin.Web's
    /// <c>/operational-alerts</c> list + details + resolve flow needs a deterministic pool of
    /// pre-seeded rows to act on. Fixed GUIDs so the E2E page objects can navigate straight to a
    /// known details route.
    /// </summary>
    public static async Task SeedE2eOperationalAlertsAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        var resolvedId = Guid.Parse("0000e2ea-0000-0000-0000-0000000000f0");
        if (await db.AdministrativeAlerts.AnyAsync(a => a.Id == resolvedId))
            return;

        var companyId = Guid.Parse("00000000-0000-0000-0000-000000000001"); // Acme
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

        // Pool of open ReportGeneration alerts — resolve tests consume one each.
        for (var i = 1; i <= 8; i++)
        {
            var id = Guid.Parse($"0000e2ea-0000-0000-0000-0000000000{i:D2}");
            db.AdministrativeAlerts.Add(AdministrativeAlert.Raise(
                id, Cmd(companyId, $"e2e-op-alert-report-{i:D2}", AdministrativeAlertCategory.ReportGeneration, now.AddHours(-i)), now.AddHours(-i)));
        }

        // One open Compliance alert — category-filter test.
        db.AdministrativeAlerts.Add(AdministrativeAlert.Raise(
            Guid.Parse("0000e2ea-0000-0000-0000-00000000c001"),
            Cmd(companyId, "e2e-op-alert-compliance-01", AdministrativeAlertCategory.Compliance, now.AddHours(-9)),
            now.AddHours(-9)));

        // One already-resolved alert — "no Resolve button on a resolved alert" test.
        var resolved = AdministrativeAlert.Raise(
            resolvedId,
            Cmd(companyId, "e2e-op-alert-resolved-01", AdministrativeAlertCategory.ReportGeneration, now.AddHours(-48)),
            now.AddHours(-48));
        resolved.Resolve(Guid.Parse("00000000-0000-0000-0000-0000000000aa"), "E2E: resolved during seed.", now.AddHours(-47));
        db.AdministrativeAlerts.Add(resolved);

        await db.SaveChangesAsync();
    }
}
