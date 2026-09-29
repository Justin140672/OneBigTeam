using Hangfire;
using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Notifications.Jobs;

[AutomaticRetry(Attempts = 0)]
internal sealed class PurgeExpiredReadNotificationsJob(
    NotificationsDbContext db,
    IClock clock,
    IConfiguration configuration,
    ILegalHoldStatusReader legalHoldStatusReader,
    IAuditEventPublisher auditPublisher,
    IAdministrativeAlertWriter administrativeAlertWriter,
    ILogger<PurgeExpiredReadNotificationsJob> logger)
{
    public const int DefaultRetentionDays = 365;

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var retentionDays = configuration.GetValue<int?>("Notifications:Retention:RetentionDays")
            ?? DefaultRetentionDays;
        var isEnabled = configuration.GetValue<bool?>("Notifications:Retention:Enabled") == true;
        var dryRun = !isEnabled;

        var now = clock.UtcNowOffset();
        var cutoff = now.AddDays(-retentionDays);

        try
        {
            var companyIds = await db.Notifications
                .Where(n => n.IsRead && n.CreatedAt < cutoff)
                .Select(n => n.CompanyId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (companyIds.Count == 0)
            {
                logger.LogInformation(
                    "PurgeExpiredReadNotificationsJob: nothing eligible (cutoff {Cutoff:u}, dryRun {DryRun}).",
                    cutoff, dryRun);
                return;
            }

            var totalDeleted = 0;

            foreach (var companyId in companyIds)
            {
                var eligible = await db.Notifications
                    .Where(n => n.CompanyId == companyId && n.IsRead && n.CreatedAt < cutoff)
                    .ToListAsync(cancellationToken);

                var count = eligible.Count;

                if (await legalHoldStatusReader.IsUnderLegalHoldAsync(companyId, cancellationToken))
                {
                    logger.LogInformation(
                        "PurgeExpiredReadNotificationsJob: company {CompanyId} under legal hold — {Count} read notification(s) preserved.",
                        companyId, count);
                    await auditPublisher.PublishAsync(new NotificationsRetentionRunAuditEvent(
                        companyId, now, dryRun, retentionDays, cutoff,
                        NotificationsDeleted: count, SkippedDueToLegalHold: true), cancellationToken);
                    continue;
                }

                if (dryRun)
                {
                    logger.LogInformation(
                        "PurgeExpiredReadNotificationsJob (dry run): would delete {Count} read notification(s) older than {Cutoff:u} for company {CompanyId}.",
                        count, cutoff, companyId);
                }
                else
                {
                    db.Notifications.RemoveRange(eligible);
                    await db.SaveChangesAsync(cancellationToken);
                    logger.LogInformation(
                        "PurgeExpiredReadNotificationsJob: deleted {Count} read notification(s) older than {Cutoff:u} for company {CompanyId}.",
                        count, cutoff, companyId);
                }

                totalDeleted += count;

                await auditPublisher.PublishAsync(new NotificationsRetentionRunAuditEvent(
                    companyId, now, dryRun, retentionDays, cutoff,
                    NotificationsDeleted: count, SkippedDueToLegalHold: false), cancellationToken);
            }

            logger.LogInformation(
                "PurgeExpiredReadNotificationsJob complete: {CompanyCount} companies, {TotalDeleted} notification(s) {Verb} (dryRun {DryRun}).",
                companyIds.Count, totalDeleted, dryRun ? "matched" : "deleted", dryRun);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PurgeExpiredReadNotificationsJob failed.");
            try
            {
                await administrativeAlertWriter.RaiseAsync(new RaiseAdministrativeAlertCommand(
                    CompanyId: Guid.Empty,
                    Severity: AdministrativeAlertSeverity.Warning,
                    Category: AdministrativeAlertCategory.Compliance,
                    Summary: "Scheduled notifications retention job failed",
                    Detail: "The read-notification retention sweep did not complete. Data-retention obligations may not have been applied on schedule.",
                    OccurredAt: DateTimeOffset.UtcNow,
                    DedupKey: "compliance:notifications-retention-job-failure",
                    AffectedEntityType: "RetentionJob",
                    AffectedEntityId: null,
                    RecommendedAction: "Review the Hangfire dashboard and job logs, then re-run.",
                    ActionUrl: null), CancellationToken.None);
            }
            catch (Exception alertEx)
            {
                logger.LogWarning(alertEx, "PurgeExpiredReadNotificationsJob: failed to raise failure alert.");
            }
            throw;
        }
    }
}
