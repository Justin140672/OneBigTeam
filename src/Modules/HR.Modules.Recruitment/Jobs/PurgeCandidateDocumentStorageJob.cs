using Hangfire;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Recruitment.Jobs;

/// <summary>
/// Ticket 7 (P2) / Ticket 13 (P2): deletes a purged candidate's uploaded document from blob
/// storage, driven by the durable <see cref="CandidateDocumentDeletionOperation"/> persisted in the
/// SAME transaction as the CandidateDocument row deletion and candidate redaction (see
/// Features/PurgeEligibleCandidates/Handler.cs) — the operation row, not this job's own enqueue, is
/// what makes the deletion recoverable after a crash; see
/// Jobs/PurgeCandidateDocumentStorageReconciliationJob.cs for the recurring sweep that repairs a
/// missed/failed enqueue or an interrupted attempt.
///
/// Idempotent: re-checks the operation's own status before attempting a delete (a Completed
/// operation is a no-op), and storage deletion of an already-deleted/nonexistent key is treated as
/// success — so a duplicate worker/enqueue (this job's own retry, and the reconciliation sweep
/// racing it) can never fail or double-report a failure for work already done.
///
/// Ticket 18 (P1): PurgeEligibleCandidates' own legal-hold check only guards the INITIAL purge
/// request — a hold placed any time afterwards (including between a prior successful DB-level
/// purge and this specific blob-delete attempt) was never rechecked here, so physical deletion
/// could proceed regardless. This worker now re-checks <see cref="ILegalHoldStatusReader"/>
/// immediately before the actual destructive <see cref="ICandidateDocumentStorageService.DeleteAsync"/>
/// call, every single attempt — not only at enqueue time.
///
/// Ticket 19 (P2): claims are guarded by the same optimistic-concurrency primitive as
/// <see cref="HR.Modules.Identity.Domain.AccountDisablement"/> — see that type's remarks. Two entry
/// points mirror AccountDisablementJob's:
///  - A LIVE dispatch (<see cref="ProcessAsync(Guid)"/>, enqueued directly by
///    PurgeEligibleCandidatesHandler for a fresh Pending row) claims the row itself.
///  - A RECONCILIATION re-enqueue (<see cref="ProcessAsync(Guid,Guid)"/>,
///    PurgeCandidateDocumentStorageReconciliationJob) has already atomically claimed the row before
///    enqueuing — this overload only verifies the claim is still valid before proceeding.
/// </summary>
[AutomaticRetry(Attempts = MaxAttempts, DelaysInSeconds = new[] { 30, 120, 600, 1800 })]
internal sealed class PurgeCandidateDocumentStorageJob(
    RecruitmentDbContext db,
    ICandidateDocumentStorageService storage,
    ILegalHoldStatusReader legalHoldStatusReader,
    IAuditEventPublisher auditPublisher,
    IClock clock,
    ILogger<PurgeCandidateDocumentStorageJob> logger)
{
    public const int MaxAttempts = 5;

    /// <summary>Live-dispatch entry point — claims the row itself (see class remarks).</summary>
    public Task ProcessAsync(Guid operationId) =>
        ProcessCoreAsync(operationId, alreadyClaimedBy: null);

    /// <summary>Reconciliation entry point — <paramref name="claimedBy"/> is the id
    /// PurgeCandidateDocumentStorageReconciliationJob already atomically claimed this row under;
    /// verified (not re-claimed) before proceeding.</summary>
    public Task ProcessAsync(Guid operationId, Guid claimedBy) =>
        ProcessCoreAsync(operationId, alreadyClaimedBy: claimedBy);

    private async Task ProcessCoreAsync(Guid operationId, Guid? alreadyClaimedBy)
    {
        var operation = await db.CandidateDocumentDeletionOperations
            .SingleOrDefaultAsync(o => o.Id == operationId);

        if (operation is null)
        {
            logger.LogWarning(
                "PurgeCandidateDocumentStorageJob: no deletion operation found for id {OperationId} — skipping.",
                operationId);
            return;
        }

        if (operation.Status == CandidateDocumentDeletionOperation.StatusCompleted)
            return;

        var now = clock.UtcNowOffset();

        // Ticket 18 (P1): checked immediately before the actual destructive delete, on every
        // attempt — not only at enqueue time. Ticket 19 (P2): deliberately checked BEFORE
        // claiming/verifying ownership — Claim() always advances AttemptCount (it IS the "an
        // attempt is being made" signal), and a hold must never consume a retry attempt. Guarded by
        // the same optimistic-concurrency check as a claim, so two racing workers/reconcilers can
        // never both suspend (or one suspend while another proceeds to delete) the same row.
        if (await legalHoldStatusReader.IsUnderLegalHoldAsync(operation.CompanyId, CancellationToken.None))
        {
            var wasAlreadyHeld = operation.Status == CandidateDocumentDeletionOperation.StatusHeld;
            var expectedVersionForHold = operation.Version;
            operation.MarkHeld(now);

            var holdResult = await db.SaveChangesWithConcurrencyAsync(
                operation, expectedVersionForHold,
                "This document deletion operation was already claimed by another worker.",
                CancellationToken.None);

            if (holdResult.IsFailure)
            {
                logger.LogInformation(
                    "PurgeCandidateDocumentStorageJob: lost the claim race suspending operation {OperationId} (company {CompanyId}) for legal hold — another worker already owns it.",
                    operationId, operation.CompanyId);
                return;
            }

            if (!wasAlreadyHeld)
            {
                logger.LogInformation(
                    "PurgeCandidateDocumentStorageJob: suspending deletion operation {OperationId} (candidate {CandidateId}, company {CompanyId}) — company is under legal hold.",
                    operation.Id, operation.CandidateId, operation.CompanyId);

                await auditPublisher.PublishAsync(
                    new CandidateDocumentDeletionSuspendedForLegalHoldAuditEvent(
                        operation.CompanyId, operation.Id, operation.CandidateId, operation.StorageKey, now),
                    CancellationToken.None);
            }

            return;
        }

        if (alreadyClaimedBy is { } claimId)
        {
            // Ticket 19 (P2): the reconciler already won the claim race for this row atomically —
            // just verify that claim is still live rather than claiming again.
            if (!operation.IsClaimedBy(claimId, now))
            {
                logger.LogInformation(
                    "PurgeCandidateDocumentStorageJob: reconciler's claim for operation {OperationId} (company {CompanyId}) is no longer valid — another worker has since claimed it. Skipping.",
                    operationId, operation.CompanyId);
                return;
            }
        }
        else
        {
            var expectedVersion = operation.Version;
            operation.Claim(Guid.NewGuid(), now);

            var claimResult = await db.SaveChangesWithConcurrencyAsync(
                operation, expectedVersion,
                "This document deletion operation is already being processed by another worker.",
                CancellationToken.None);

            if (claimResult.IsFailure)
            {
                logger.LogInformation(
                    "PurgeCandidateDocumentStorageJob: lost the claim race for operation {OperationId} (company {CompanyId}) — another worker already owns it.",
                    operationId, operation.CompanyId);
                return;
            }
        }

        try
        {
            await storage.DeleteAsync(operation.StorageKey, CancellationToken.None);

            operation.MarkCompleted(clock.UtcNowOffset());
            await db.SaveChangesAsync();

            logger.LogInformation(
                "PurgeCandidateDocumentStorageJob: deleted storage key for operation {OperationId} (candidate {CandidateId}, company {CompanyId}).",
                operation.Id, operation.CandidateId, operation.CompanyId);
        }
        catch (Exception ex)
        {
            var isFinalAttempt = operation.AttemptCount >= MaxAttempts;

            if (isFinalAttempt)
            {
                operation.MarkFailed(ex.Message, clock.UtcNowOffset(), MaxAttempts);
                await db.SaveChangesAsync();

                logger.LogError(ex,
                    "PurgeCandidateDocumentStorageJob: permanently failed to delete storage key for operation {OperationId} (candidate {CandidateId}, company {CompanyId}) after {Attempts} attempts — retained for manual remediation.",
                    operation.Id, operation.CandidateId, operation.CompanyId, MaxAttempts);
            }
            else
            {
                logger.LogWarning(ex,
                    "PurgeCandidateDocumentStorageJob: attempt {AttemptCount} failed for operation {OperationId} — will retry.",
                    operation.AttemptCount, operation.Id);
            }

            throw;
        }
    }
}
