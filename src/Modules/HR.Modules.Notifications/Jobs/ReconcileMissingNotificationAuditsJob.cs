using Hangfire;
using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Notifications.Jobs;

/// <summary>
/// OBT-REM-12/OBT-REM-14: bounded reconciliation for creation audits that may have been lost when a
/// caller committed a Notification but crashed before <see cref="NotificationsAudit.NotificationCreatedAuditEvent"/>
/// was published. Deliberately re-checks and republishes rather than assuming loss — because
/// NotificationCreatedAuditEvent's EventId is deterministic (== NotificationId, see NotificationsAudit),
/// republishing an event that was already recorded is a guaranteed no-op (unique EventId constraint
/// on both the audit staging and committed tables), so this job never needs its own additional
/// idempotency bookkeeping for the audit event itself.
///
/// Safety controls:
/// <list type="bullet">
///   <item><b>Grace period.</b> Only notifications older than <see cref="GraceMinutes"/> are
///   considered, so a notification whose audit publish is merely still in flight on the normal path
///   is never touched.</item>
///   <item><b>Lookback window.</b> Only notifications created within <see cref="LookbackHours"/> are
///   scanned — this is recovery for recent near-miss failures, not a general historical backfill.</item>
///   <item><b>Tenant-safe, keyset-paginated, bounded.</b> Processed per company, capped at
///   <see cref="BatchSizePerCompany"/> per company per run. Candidate selection uses keyset
///   pagination (see <see cref="NotificationAuditReconciliationCursor"/>) driven by a durable
///   per-company cursor rather than a fixed offset/Take over a query that returns the same rows every
///   run — this guarantees forward progress even when the oldest candidates in the window are already
///   audited: OBT-REM-14 fixed a bug where a run-after-run-identical Take(N) meant a genuinely missing
///   audit past the first N already-audited notifications could never be reached. The cursor is
///   scoped per company, so one busy tenant's backlog cannot consume another tenant's scan budget.</item>
///   <item><b>Existence-checked before republish.</b> Uses <see cref="IAuditEventExistenceReader"/>
///   (Infrastructure.Abstractions bridge to the audit store) to skip notifications that already have
///   a committed audit event, keeping normal runs cheap and log-quiet even though a republish would
///   itself be safe.</item>
///   <item><b>Crash-safe progress.</b> The cursor is only advanced after the batch's audit checks
///   (and any repairs) have completed, and is persisted via the same DbContext/SaveChanges as the
///   rest of the run. If the process crashes after publishing a repaired audit event but before the
///   cursor is saved, the next run rescans the same range: the existence check finds the
///   already-published (deterministic-EventId) audit event and skips it, so no duplicate is created
///   and no progress is lost — the cursor simply advances one run later than it "could" have.</item>
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

            var cursor = await db.NotificationAuditReconciliationCursors
                .FirstOrDefaultAsync(c => c.CompanyId == companyId, cancellationToken);

            // A cursor left over from a previous, now-expired window (its resume point predates the
            // current lookback start) is stale: the window has slid forward, so resume from the
            // start of the current window rather than a position that no longer exists.
            var resumeFromStart = cursor is null || cursor.LastScannedCreatedAt < lookback;
            var resumeCreatedAt = resumeFromStart ? lookback : cursor!.LastScannedCreatedAt;
            var resumeId = resumeFromStart ? Guid.Empty : cursor!.LastScannedNotificationId;

            // Keyset pagination: strictly-after the last scanned (CreatedAt, Id) pair. Using
            // resumeFromStart lets the first page of a fresh window include a notification whose
            // CreatedAt exactly equals lookback (inclusive lower bound), matching prior behaviour.
            var candidates = await db.Notifications
                .AsNoTracking()
                .Where(n => n.CompanyId == companyId && n.CreatedAt >= lookback && n.CreatedAt < cutoff)
                .Where(n => resumeFromStart
                    || n.CreatedAt > resumeCreatedAt
                    || (n.CreatedAt == resumeCreatedAt && n.Id > resumeId))
                .OrderBy(n => n.CreatedAt).ThenBy(n => n.Id)
                .Take(BatchSizePerCompany)
                .Select(n => new { n.Id, n.EmployeeId, n.Type, n.CreatedAt })
                .ToListAsync(cancellationToken);

            if (candidates.Count == 0)
            {
                // Caught up to the end of the current window — reset so the next run restarts from
                // the (now further-forward-slid) beginning of the window instead of resuming from a
                // position that no longer yields anything.
                if (cursor is not null)
                {
                    cursor.Reset(now);
                    await db.SaveChangesAsync(cancellationToken);
                }

                continue;
            }

            var repaired = 0;

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Deterministic EventId (see NotificationCreatedAuditEvent) — the notification id
                // itself is the audit event id, so existence can be checked directly.
                var alreadyAudited = await auditEventExistenceReader.ExistsAsync(candidate.Id, cancellationToken);
                if (!alreadyAudited)
                {
                    var channel = NotificationChannelDefaults.GetChannel(candidate.Type);
                    await auditPublisher.PublishAsync(new NotificationCreatedAuditEvent(
                        companyId, candidate.Id, candidate.EmployeeId, candidate.Type, channel, candidate.CreatedAt),
                        cancellationToken);

                    repaired++;
                }
            }

            var last = candidates[^1];
            if (cursor is null)
            {
                cursor = NotificationAuditReconciliationCursor.Create(companyId, last.CreatedAt, last.Id, now);
                db.NotificationAuditReconciliationCursors.Add(cursor);
            }
            else
            {
                cursor.Advance(last.CreatedAt, last.Id, now);
            }

            await db.SaveChangesAsync(cancellationToken);

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
