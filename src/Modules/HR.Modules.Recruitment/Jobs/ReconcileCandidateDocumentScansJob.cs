using Hangfire;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Recruitment.Jobs;

/// <summary>
/// [P1] Recurring backstop that guarantees no candidate document is left permanently unscanned —
/// the recruitment counterpart of the module's other reconciliation sweeps
/// (PurgeCandidateDocumentStorageReconciliationJob, OffboardingPlanCreationReconciliationJob):
/// <list type="bullet">
/// <item><description><b>Due Pending rows</b> — a lost/never-sent enqueue, a lost retry schedule, or a
/// row backfilled to Pending by the CandidateDocumentMalwareScan migration — are re-dispatched to
/// <see cref="ScanCandidateDocumentJob"/>. A freshly uploaded row gets a short grace period first
/// so the normal on-save dispatch is not duplicated.</description></item>
/// <item><description><b>Abandoned Scanning claims</b> (lease expired: the worker crashed or was
/// recycled mid-scan) are released — back to Pending for immediate re-dispatch while attempts remain,
/// otherwise terminally Failed — and audited.</description></item>
/// </list>
/// Every re-dispatch still goes through the job's own claim, so attempts stay bounded by
/// <see cref="CandidateDocument.MaxScanAttempts"/> however often this runs. Batched, so a large
/// backfill drains over several sweeps rather than flooding the scanner.
/// </summary>
internal sealed class ReconcileCandidateDocumentScansJob(
    RecruitmentDbContext db,
    IBackgroundJobClient backgroundJobClient,
    IAuditEventPublisher auditPublisher,
    IClock clock,
    ILogger<ReconcileCandidateDocumentScansJob> logger)
{
    internal const int BatchSize = 500;

    /// <summary>A new upload is normally dispatched on save; only sweep it after this grace period.</summary>
    internal static readonly TimeSpan NewUploadGracePeriod = TimeSpan.FromMinutes(2);

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ExecuteAsync()
    {
        var released = await ReleaseAbandonedScansAsync();
        var dispatched = await DispatchDuePendingScansAsync();

        if (released > 0 || dispatched > 0)
        {
            logger.LogWarning(
                "ReconcileCandidateDocumentScansJob: released {Released} abandoned scan claim(s) and re-dispatched {Dispatched} pending candidate document scan(s).",
                released, dispatched);
        }
    }

    private async Task<int> ReleaseAbandonedScansAsync()
    {
        var now = clock.UtcNowOffset();
        var leaseCutoff = now - CandidateDocument.ScanLeaseDuration;

        var abandoned = await db.CandidateDocuments
            .Where(d => d.ScanStatus == CandidateDocumentScanStatus.Scanning
                && (d.ScanLastAttemptAt == null || d.ScanLastAttemptAt <= leaseCutoff))
            .OrderBy(d => d.CreatedAt)
            .Take(BatchSize)
            .ToListAsync();

        var released = 0;
        foreach (var document in abandoned)
        {
            var attempt = document.ScanAttemptCount;
            document.ReleaseAbandonedScan(now);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                // A worker claimed or finished it in the meantime — nothing to repair.
                db.Entry(document).State = EntityState.Detached;
                continue;
            }

            released++;
            logger.LogWarning(
                "ReconcileCandidateDocumentScansJob: scan attempt {Attempt} for candidate document {DocumentId} (company {CompanyId}) was abandoned; document is now {Status}.",
                attempt, document.Id, document.CompanyId, document.ScanStatus);

            await PublishAuditAsync(new CandidateDocumentScanStatusChangedAuditEvent(
                document.CompanyId, document.Id, document.CandidateId,
                CandidateDocumentScanStatus.Scanning.ToString(), document.ScanStatus.ToString(),
                attempt, document.ScanFailureReason, now));

            if (document.ScanStatus == CandidateDocumentScanStatus.Failed)
            {
                logger.LogCritical(
                    "ReconcileCandidateDocumentScansJob: candidate document {DocumentId} (company {CompanyId}) exhausted its {MaxAttempts} scan attempts and is blocked from download.",
                    document.Id, document.CompanyId, CandidateDocument.MaxScanAttempts);
            }
        }

        return released;
    }

    private async Task<int> DispatchDuePendingScansAsync()
    {
        var now = clock.UtcNowOffset();
        var newUploadCutoff = now - NewUploadGracePeriod;

        var dueIds = await db.CandidateDocuments
            .AsNoTracking()
            .Where(d => d.ScanStatus == CandidateDocumentScanStatus.Pending
                && (d.ScanNextAttemptAt != null
                    ? d.ScanNextAttemptAt <= now
                    : d.CreatedAt <= newUploadCutoff))
            .OrderBy(d => d.CreatedAt)
            .Select(d => d.Id)
            .Take(BatchSize)
            .ToListAsync();

        var dispatched = 0;
        foreach (var documentId in dueIds)
        {
            try
            {
                backgroundJobClient.Enqueue<ScanCandidateDocumentJob>(job => job.ScanAsync(documentId));
                dispatched++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "ReconcileCandidateDocumentScansJob: failed to dispatch scan for candidate document {DocumentId}; will retry on the next sweep.",
                    documentId);
            }
        }

        return dispatched;
    }

    private async Task PublishAuditAsync<TEvent>(TEvent auditEvent) where TEvent : IAuditEvent
    {
        try
        {
            await auditPublisher.PublishAsync(auditEvent, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ReconcileCandidateDocumentScansJob: failed to publish audit event {EventType}.", typeof(TEvent).Name);
        }
    }
}
