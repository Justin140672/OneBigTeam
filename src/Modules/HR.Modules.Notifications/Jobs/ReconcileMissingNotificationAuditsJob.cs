using Hangfire;
using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Notifications.Jobs;

/// <summary>
/// OBT-REM-12: bounded reconciliation for creation audits that may have been lost when a caller
/// committed a Notification but crashed before <see cref="NotificationsAudit.NotificationCreatedAuditEvent"/>
/// was published. Deliberately re-checks and republishes rather than assuming loss — because
/// NotificationCreatedAuditEvent's EventId is deterministic (== NotificationId, see NotificationsAudit),
/// republishing an event that was already recorded is a guaranteed no-op (unique EventId constraint
/// on both the audit staging and committed tables), so this job never needs its own additional
/// idempotency bookkeeping.
///
/// Safety controls:
/// <list type="bullet">
///   <item><b>Grace period.</b> Only notifications older than <see cref="GraceMinutes"/> are
///   considered, so a notification whose audit publish is merely still in flight on the normal path
///   is never touched.</item>
///   <item><b>Lookback window.</b> Only notifications created within <see cref="LookbackHours"/> are
///   scanned — this is recovery for recent near-miss failures, not a general historical backfill.</item>
///   <item><b>Tenant-safe, paginated, bounded.</b> Processed per company, capped at
///   <see cref="BatchSizePerCompany"/> per company per run.</item>
///   <item><b>Existence-checked before republish.</b> Uses <see cref="IAuditEventExistenceReader"/>
///   (Infrastructure.Abstractions bridge to the audit store) to skip notifications that already have
///   a committed audit event, keeping normal runs cheap and log-quiet even though a republish would
///   itself be safe.</item>
/// </list>
/// </summary>
[AutomaticRetry(Attempts = 0)]
internal sealed class ReconcileMissingNotificationAuditsJob(
    NotificationsDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    IAuditEventExistenceReader auditEventExistenceReader,
    ILogger<ReconcileMissingNotificationAuditsJob> logger)
{
    public const int GraceMinutes = 15;
    public const int LookbackHours = 24;
    public const int BatchSizePerCompany = 200;

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNowOffset();
        var cutoff = now.AddMinutes(-GraceMinutes);
        var lookback = now.AddHours(-LookbackHours);

        var companyIds = await db.Notifications
            .Where(n => n.CreatedAt >= lookback && n.CreatedAt < cutoff)
            .Select(n => n.CompanyId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (companyIds.Count == 0)
        {
            logger.LogInformation(
                "ReconcileMissingNotificationAuditsJob: nothing in window (lookback {Lookback:u} - cutoff {Cutoff:u}).",
                lookback, cutoff);
            return;
        }

        var totalRepaired = 0;

        foreach (var companyId in companyIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidates = await db.Notifications
                .AsNoTracking()
                .Where(n => n.CompanyId == companyId && n.CreatedAt >= lookback && n.CreatedAt < cutoff)
                .OrderBy(n => n.CreatedAt)
                .Take(BatchSizePerCompany)
                .Select(n => new { n.Id, n.EmployeeId, n.Type, n.CreatedAt })
                .ToListAsync(cancellationToken);

            var repaired = 0;

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Deterministic EventId (see NotificationCreatedAuditEvent) — the notification id
                // itself is the audit event id, so existence can be checked directly.
                var alreadyAudited = await auditEventExistenceReader.ExistsAsync(candidate.Id, cancellationToken);
                if (alreadyAudited)
                    continue;

                var channel = NotificationChannelDefaults.GetChannel(candidate.Type);
                await auditPublisher.PublishAsync(new NotificationCreatedAuditEvent(
                    companyId, candidate.Id, candidate.EmployeeId, candidate.Type, channel, candidate.CreatedAt),
                    cancellationToken);

                repaired++;
            }

            totalRepaired += repaired;

            if (repaired > 0)
            {
                logger.LogInformation(
                    "ReconcileMissingNotificationAuditsJob: repaired {Count} missing creation audit(s) for company {CompanyId}.",
                    repaired, companyId);
            }
        }

        logger.LogInformation(
            "ReconcileMissingNotificationAuditsJob complete: {CompanyCount} companies scanned, {TotalRepaired} audit(s) repaired.",
            companyIds.Count, totalRepaired);
    }
}
