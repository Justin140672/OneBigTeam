using Hangfire;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Documents.Jobs;

/// <summary>
/// Recovers malware scans that were committed but never completed: redispatches Pending work that
/// was never picked up (crash between commit and enqueue, lost Hangfire job), releases Scanning
/// claims whose lease expired, and moves work that has used all of its attempts into the safe
/// terminal Failed state. Bounded batches; safe to run concurrently with scan jobs because every
/// state change is guarded by the work item's version token.
/// </summary>
internal sealed class ReconcileFileScansJob(
    DocumentsDbContext db,
    IBackgroundJobClient backgroundJobClient,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    FileScanBacklogReader backlogReader,
    ILogger<ReconcileFileScansJob> logger)
{
    internal const int BatchSize = 100;
    internal static readonly TimeSpan DispatchGrace = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan RedispatchDelay = TimeSpan.FromMinutes(10);

    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    public async Task ExecuteAsync()
    {
        var now = clock.UtcNowOffset();

        var staleClaims = await RecoverStaleClaimsAsync(now);
        var redispatched = await RedispatchPendingAsync(now);

        if (staleClaims > 0 || redispatched > 0)
        {
            logger.LogWarning(
                "ReconcileFileScansJob: recovered {StaleClaims} stale claims and redispatched {Redispatched} pending scans.",
                staleClaims, redispatched);
        }

        var backlog = await backlogReader.ReadAsync(CancellationToken.None);
        logger.LogInformation(
            "File scan backlog: pending={Pending} oldestPendingAgeSeconds={OldestAge} scanning={Scanning} staleClaims={Stale} retried={Retried} atMaxAttempts={AtMax} exhausted={Exhausted}.",
            backlog.PendingCount, backlog.OldestPendingAgeSeconds, backlog.ScanningCount, backlog.StaleClaimCount,
            backlog.RetriedCount, backlog.MaxAttemptCount, backlog.ExhaustedCount);
    }

    private async Task<int> RecoverStaleClaimsAsync(DateTimeOffset now)
    {
        var ids = await db.FileScanWork
            .Where(w => w.State == FileScanWorkState.Scanning && w.LeaseExpiresAt != null && w.LeaseExpiresAt <= now)
            .OrderBy(w => w.LeaseExpiresAt)
            .Take(BatchSize)
            .Select(w => w.Id)
            .ToListAsync();

        var recovered = 0;
        foreach (var id in ids)
        {
            try
            {
                db.ChangeTracker.Clear();
                var work = await db.FileScanWork.SingleOrDefaultAsync(w => w.Id == id);
                if (work is null || work.State != FileScanWorkState.Scanning || work.LeaseExpiresAt is null || work.LeaseExpiresAt > now)
                {
                    continue;
                }

                if (work.HasExhaustedAttempts)
                {
                    if (await ExhaustAsync(work, now))
                    {
                        recovered++;
                    }

                    continue;
                }

                var expected = work.Version;
                work.ReleaseForImmediateRetry("stale_claim", now);
                var save = await db.SaveChangesWithConcurrencyAsync(
                    work, expected, "The scan work item changed during stale-claim recovery.", CancellationToken.None);
                if (save.IsFailure)
                {
                    continue;
                }

                recovered++;
                logger.LogWarning(
                    "ReconcileFileScansJob: released stale scan claim for {TargetType} {EntityId} (attempt {Attempt}).",
                    work.TargetType, work.EntityId, work.AttemptCount);

                await auditPublisher.PublishAsync(new FileScanRecoveredAuditEvent(
                    work.CompanyId, work.TargetType.ToString(), work.EntityId, "stale_claim_released", work.AttemptCount, now),
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ReconcileFileScansJob: failed to recover stale claim {WorkId}.", id);
            }
        }

        return recovered;
    }

    private async Task<bool> ExhaustAsync(FileScanWork work, DateTimeOffset now)
    {
        var target = await FileScanTargets.LoadAsync(db, work.TargetType, work.EntityId, CancellationToken.None);
        var previousStatus = target?.ScanStatus.ToString() ?? FileScanStatus.Scanning.ToString();
        var expected = work.Version;

        target?.MarkScanFailed("scan_attempts_exhausted", now);
        work.Exhaust("scan_attempts_exhausted", now);

        var save = await db.SaveChangesWithConcurrencyAsync(
            work, expected, "The scan work item changed while exhausting it.", CancellationToken.None);
        if (save.IsFailure)
        {
            return false;
        }

        logger.LogCritical(
            "ReconcileFileScansJob: scan attempts exhausted for {TargetType} {EntityId} (Company {CompanyId}); file marked Failed.",
            work.TargetType, work.EntityId, work.CompanyId);

        await auditPublisher.PublishAsync(new FileScanStatusChangedAuditEvent(
            work.CompanyId, work.TargetType.ToString(), work.EntityId, target?.EmployeeId,
            previousStatus, FileScanStatus.Failed.ToString(), "scan_attempts_exhausted", now), CancellationToken.None);

        return true;
    }

    private async Task<int> RedispatchPendingAsync(DateTimeOffset now)
    {
        var dueBefore = now - DispatchGrace;
        var ids = await db.FileScanWork
            .Where(w => w.State == FileScanWorkState.Pending && w.NextAttemptAt <= dueBefore)
            .OrderBy(w => w.NextAttemptAt)
            .Take(BatchSize)
            .Select(w => w.Id)
            .ToListAsync();

        var redispatched = 0;
        foreach (var id in ids)
        {
            try
            {
                db.ChangeTracker.Clear();
                var work = await db.FileScanWork.SingleOrDefaultAsync(w => w.Id == id);
                if (work is null || work.State != FileScanWorkState.Pending || work.NextAttemptAt > dueBefore)
                {
                    continue;
                }

                if (work.HasExhaustedAttempts)
                {
                    if (await ExhaustAsync(work, now))
                    {
                        redispatched++;
                    }

                    continue;
                }

                var (targetType, entityId, companyId) = (work.TargetType, work.EntityId, work.CompanyId);
                backgroundJobClient.Enqueue<ScanUploadedFileJob>(job =>
                    job.ExecuteAsync(targetType, entityId, companyId, null));

                var expected = work.Version;
                work.RecordDispatch(now, RedispatchDelay);
                var save = await db.SaveChangesWithConcurrencyAsync(
                    work, expected, "The scan work item changed while recording its redispatch.", CancellationToken.None);
                if (save.IsFailure)
                {
                    continue;
                }

                redispatched++;
                logger.LogWarning(
                    "ReconcileFileScansJob: redispatched pending scan for {TargetType} {EntityId} (dispatch {Dispatch}, attempt {Attempt}).",
                    work.TargetType, work.EntityId, work.DispatchCount, work.AttemptCount);

                await auditPublisher.PublishAsync(new FileScanRecoveredAuditEvent(
                    work.CompanyId, work.TargetType.ToString(), work.EntityId, "redispatched", work.AttemptCount, now),
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ReconcileFileScansJob: failed to redispatch scan work {WorkId}; it will be retried.", id);
            }
        }

        return redispatched;
    }
}
