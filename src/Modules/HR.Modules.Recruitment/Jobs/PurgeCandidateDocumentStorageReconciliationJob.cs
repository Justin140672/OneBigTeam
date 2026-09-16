using Hangfire;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Recruitment.Jobs;

/// <summary>
/// Ticket 13 (P2) / Ticket 18 (P1): recurring repair for two kinds of work left behind by
/// Features/PurgeEligibleCandidates/Handler.cs that must eventually complete regardless of whether
/// their immediate best-effort trigger (a Hangfire enqueue, an inline audit publish) succeeded:
///
///  - <see cref="CandidateDocumentDeletionOperation"/> rows stuck Pending (enqueue never happened
///    or was lost), stale Processing (a PurgeCandidateDocumentStorageJob attempt crashed mid-way),
///    or Failed (exhausted retries but still eligible for another sweep) are re-enqueued.
///    Held rows (see ticket 18 — suspended because the company was under legal hold) are handled
///    separately: this sweep checks whether the hold has actually lifted BEFORE touching them, so
///    a still-held operation is left completely untouched (no db write, no enqueue) rather than
///    being re-enqueued every single sweep only to immediately re-suspend itself again.
///  - <see cref="CandidatePurgeAuditDelivery"/> rows left Pending (the inline publish attempt in the
///    handler failed) are republished here directly.
///
/// Ticket 19 (P2): claim/lease protocol — same idiom as AccountDisablementReconciliationJob. Every
/// eligible row (Pending, non-terminal Failed, stale-Processing by lease expiry, or a Held row
/// whose hold has lifted) is claimed DIRECTLY to Processing via
/// <see cref="CandidateDocumentDeletionOperation.Claim"/>, guarded by the same optimistic-
/// concurrency check the worker's own claim uses — never left sitting as a bare Pending row in
/// between (see AccountDisablementReconciliationJob's remarks for why that matters: a bare status
/// reset is indistinguishable from an untouched row and a second reconciler replica could also
/// claim it). A terminally-failed record is never reset here.
/// </summary>
internal sealed class PurgeCandidateDocumentStorageReconciliationJob(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    ILegalHoldStatusReader legalHoldStatusReader,
    IBackgroundJobClient backgroundJobClient,
    ILogger<PurgeCandidateDocumentStorageReconciliationJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ExecuteAsync()
    {
        await ReconcileDocumentDeletionsAsync();
        await ReconcileHeldDocumentDeletionsAsync();
        await ReconcileAuditDeliveriesAsync();
    }

    private async Task ReconcileDocumentDeletionsAsync()
    {
        var now = clock.UtcNowOffset();
        var reconcilerInstanceId = Guid.NewGuid();

        var candidates = await db.CandidateDocumentDeletionOperations
            .Where(o =>
                o.Status == CandidateDocumentDeletionOperation.StatusPending
                || (o.Status == CandidateDocumentDeletionOperation.StatusFailed && !o.IsTerminallyFailed)
                || (o.Status == CandidateDocumentDeletionOperation.StatusProcessing
                    && (o.LeaseExpiresAt == null || o.LeaseExpiresAt < now)))
            .ToListAsync();

        var claimed = new List<CandidateDocumentDeletionOperation>();

        foreach (var operation in candidates)
        {
            var expectedVersion = operation.Version;
            var reason = operation.Status;

            if (operation.Status == CandidateDocumentDeletionOperation.StatusFailed)
                operation.ResetForRetry();

            // Ticket 19 (P2): claims the row directly — see class remarks.
            operation.Claim(reconcilerInstanceId, now);

            var result = await db.SaveChangesWithConcurrencyAsync(
                operation, expectedVersion,
                "This document deletion operation was already claimed by another reconciler.",
                CancellationToken.None);

            if (result.IsFailure)
            {
                logger.LogInformation(
                    "PurgeCandidateDocumentStorageReconciliationJob: lost the claim race for operation {OperationId} (company {CompanyId}) — skipping this sweep.",
                    operation.Id, operation.CompanyId);
                continue;
            }

            claimed.Add(operation);

            logger.LogWarning(
                "PurgeCandidateDocumentStorageReconciliationJob: claimed and enqueuing {Reason} document deletion operation {OperationId} (candidate {CandidateId}, company {CompanyId}).",
                reason, operation.Id, operation.CandidateId, operation.CompanyId);
        }

        foreach (var operation in claimed)
        {
            backgroundJobClient.Enqueue<PurgeCandidateDocumentStorageJob>(
                job => job.ProcessAsync(operation.Id, reconcilerInstanceId));
        }
    }

    /// <summary>
    /// Ticket 18 (P1) / Ticket 19 (P2): Held operations are re-checked here — rather than left for
    /// <see cref="ReconcileDocumentDeletionsAsync"/>'s unconditional eligibility sweep — precisely
    /// so a still-active hold results in NO write and NO enqueue this sweep (the worker itself
    /// would just immediately re-suspend it, achieving nothing but churn). Only once
    /// <see cref="ILegalHoldStatusReader"/> confirms the hold has actually lifted does this claim
    /// the operation (straight to Processing, ticket 19) and hand it back to the worker.
    /// </summary>
    private async Task ReconcileHeldDocumentDeletionsAsync()
    {
        var held = await db.CandidateDocumentDeletionOperations
            .Where(o => o.Status == CandidateDocumentDeletionOperation.StatusHeld)
            .ToListAsync();

        if (held.Count == 0)
            return;

        var now = clock.UtcNowOffset();
        var reconcilerInstanceId = Guid.NewGuid();
        var claimed = new List<CandidateDocumentDeletionOperation>();

        // Grouped by company so a company with many held documents costs one hold check each sweep,
        // not one per document.
        foreach (var group in held.GroupBy(o => o.CompanyId))
        {
            if (await legalHoldStatusReader.IsUnderLegalHoldAsync(group.Key, CancellationToken.None))
                continue;

            foreach (var operation in group)
            {
                var expectedVersion = operation.Version;
                operation.Claim(reconcilerInstanceId, now);

                var result = await db.SaveChangesWithConcurrencyAsync(
                    operation, expectedVersion,
                    "This document deletion operation was already claimed by another reconciler.",
                    CancellationToken.None);

                if (result.IsFailure)
                {
                    logger.LogInformation(
                        "PurgeCandidateDocumentStorageReconciliationJob: lost the claim race resuming held operation {OperationId} (company {CompanyId}) — skipping this sweep.",
                        operation.Id, operation.CompanyId);
                    continue;
                }

                claimed.Add(operation);

                logger.LogInformation(
                    "PurgeCandidateDocumentStorageReconciliationJob: resuming held document deletion operation {OperationId} (candidate {CandidateId}, company {CompanyId}) — legal hold lifted.",
                    operation.Id, operation.CandidateId, operation.CompanyId);
            }
        }

        foreach (var operation in claimed)
        {
            // Ticket 18 (P1): published here (the point where Held actually transitions away) —
            // deduped by this event's deterministic EventId if this ever ran twice for the same
            // operation.
            await auditPublisher.PublishAsync(
                new CandidateDocumentDeletionResumedAfterLegalHoldAuditEvent(
                    operation.CompanyId, operation.Id, operation.CandidateId, operation.StorageKey, now),
                CancellationToken.None);

            backgroundJobClient.Enqueue<PurgeCandidateDocumentStorageJob>(
                job => job.ProcessAsync(operation.Id, reconcilerInstanceId));
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
