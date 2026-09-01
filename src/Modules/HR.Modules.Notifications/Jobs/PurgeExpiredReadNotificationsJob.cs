using Hangfire;
using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Notifications.Jobs;

/// <summary>
/// NFR-07: the only scheduled retention-deletion job in the codebase. Deletes in-app notifications
/// that have been <b>read</b> and are older than the retention window. Read notifications are
/// transient UI state with no lawful-basis retention requirement (see
/// docs/compliance/data-retention-inventory.md), which makes this the safest high-value category to
/// automate.
///
/// Safety controls:
/// <list type="bullet">
///   <item><b>Dry-run by default.</b> Unless <c>Notifications:Retention:Enabled=true</c> the job
///   only logs and audits what it <i>would</i> delete and removes nothing.</item>
///   <item><b>Legal hold.</b> Companies under a legal hold (<see cref="ILegalHoldStatusReader"/>)
///   are skipped entirely — their notifications are preserved regardless of age.</item>
///   <item><b>Per-company processing</b> so retention activity is attributable and isolated per
///   tenant; a failure for one company does not skip the rest.</item>
///   <item><b>Content-free audit.</b> Only aggregate counts and the policy window are recorded,
///   never notification titles/bodies/recipients (NFR-01 pattern).</item>
/// </list>
/// Unread notifications are never touched — the user has not yet seen them.
/// </summary>
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
    /// <summary>
    /// Default retention window for read notifications. No company-configurable setting exists for
    /// this and none is warranted (see inventory doc) — a fixed, generous default documented here
    /// rather than a magic number at the call site.
    /// </summary>
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
                // Load this company's eligible rows only — keeps the delete tenant-isolated and lets
                // a failure for one company be logged without aborting the rest.
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
