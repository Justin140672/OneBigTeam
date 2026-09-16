using Hangfire;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Recruitment.Jobs;

/// <summary>
/// Ticket 13 (P2): recurring repair for two kinds of work left behind by
/// Features/PurgeEligibleCandidates/Handler.cs that must eventually complete regardless of whether
/// their immediate best-effort trigger (a Hangfire enqueue, an inline audit publish) succeeded:
///
///  - <see cref="CandidateDocumentDeletionOperation"/> rows stuck Pending (enqueue never happened
///    or was lost), stale Processing (a PurgeCandidateDocumentStorageJob attempt crashed mid-way),
///    or Failed (exhausted retries but still eligible for another sweep) are re-enqueued.
///  - <see cref="CandidatePurgeAuditDelivery"/> rows left Pending (the inline publish attempt in the
///    handler failed) are republished here directly.
/// </summary>
internal sealed class PurgeCandidateDocumentStorageReconciliationJob(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    IBackgroundJobClient backgroundJobClient,
    ILogger<PurgeCandidateDocumentStorageReconciliationJob> logger)
{
    /// <summary>A Processing deletion operation older than this is assumed to belong to a crashed
    /// attempt rather than one genuinely still in flight.</summary>
    private static readonly TimeSpan StaleProcessingThreshold = TimeSpan.FromMinutes(15);

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ExecuteAsync()
    {
        await ReconcileDocumentDeletionsAsync();
        await ReconcileAuditDeliveriesAsync();
    }

    private async Task ReconcileDocumentDeletionsAsync()
    {
        var now = clock.UtcNowOffset();
        var cutoff = now - StaleProcessingThreshold;

        var stale = await db.CandidateDocumentDeletionOperations
            .Where(o =>
                o.Status == CandidateDocumentDeletionOperation.StatusPending
                || o.Status == CandidateDocumentDeletionOperation.StatusFailed
                || (o.Status == CandidateDocumentDeletionOperation.StatusProcessing
                    && (o.LastAttemptAt == null || o.LastAttemptAt < cutoff)))
            .ToListAsync();

        foreach (var operation in stale)
        {
            if (operation.Status == CandidateDocumentDeletionOperation.StatusFailed)
                operation.ResetForRetry();
            else if (operation.Status == CandidateDocumentDeletionOperation.StatusProcessing)
                operation.ResetToPendingAfterInterruption();

            logger.LogWarning(
                "PurgeCandidateDocumentStorageReconciliationJob: re-enqueuing stale document deletion operation {OperationId} (candidate {CandidateId}, company {CompanyId}).",
                operation.Id, operation.CandidateId, operation.CompanyId);
        }

        if (stale.Count > 0)
            await db.SaveChangesAsync();

        foreach (var operation in stale)
        {
            backgroundJobClient.Enqueue<PurgeCandidateDocumentStorageJob>(job => job.ProcessAsync(operation.Id));
        }
    }

    private async Task ReconcileAuditDeliveriesAsync()
    {
        var pending = await db.CandidatePurgeAuditDeliveries
            .Where(d => d.Status == CandidatePurgeAuditDelivery.StatusPending)
            .ToListAsync();

        foreach (var delivery in pending)
        {
            delivery.RecordAttempt();

            try
            {
                await auditPublisher.PublishAsync(
                    new CandidatesPurgedAuditEvent(
                        delivery.CompanyId,
                        delivery.CandidateIds.ToList(),
                        delivery.PurgedBy,
                        delivery.CreatedAt),
                    CancellationToken.None);

                delivery.MarkDelivered(clock.UtcNowOffset());

                logger.LogInformation(
                    "PurgeCandidateDocumentStorageReconciliationJob: delivered pending purge audit event {DeliveryId} (company {CompanyId}) on retry {AttemptCount}.",
                    delivery.Id, delivery.CompanyId, delivery.AttemptCount);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "PurgeCandidateDocumentStorageReconciliationJob: attempt {AttemptCount} failed to deliver purge audit event {DeliveryId} — will retry on the next sweep.",
                    delivery.AttemptCount, delivery.Id);
            }
        }

        if (pending.Count > 0)
            await db.SaveChangesAsync();
    }
}
