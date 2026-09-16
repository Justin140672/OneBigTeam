using Hangfire;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Jobs;
using HR.Modules.Recruitment.Persistence;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Features.PurgeEligibleCandidates;

/// <summary>
/// SET-05: "a separately authorised retention process can permanently redact eligible candidate
/// personal data" — mirrors Documents' PurgeEligibleArchivedEmployeeDocuments (DOC-04). Deliberately
/// not automatic: changing CandidateRetentionDays alone never destroys data (see
/// UpdateRecruitmentSettingsHandler) — only this explicit, company-administrator-gated action does,
/// and only for candidates that satisfy every eligibility condition below.
///
/// Eligible = not already purged, not linked to a hired employee, has no application sitting on a
/// non-terminal (still-in-progress) recruitment stage, and the candidate's last update is older than
/// the company's current CandidateRetentionDays window.
/// </summary>
internal sealed class PurgeEligibleCandidatesHandler(
    RecruitmentDbContext db,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    ICompanyRecruitmentSettingsReader recruitmentSettingsReader,
    ILegalHoldStatusReader legalHoldStatusReader,
    IBackgroundJobClient backgroundJobClient)
{
    public async Task<Result<PurgeEligibleCandidatesResponse>> HandleAsync(
        PurgeEligibleCandidatesRequest request,
        Guid purgedBy,
        CancellationToken cancellationToken)
    {
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await db.TryReplayAsync<IdempotencyRecord, PurgeEligibleCandidatesResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<PurgeEligibleCandidatesResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        // NFR-07: a company under legal hold is exempt from all retention deletion until lifted.
        if (await legalHoldStatusReader.IsUnderLegalHoldAsync(request.CompanyId, cancellationToken))
        {
            return Result.Failure<PurgeEligibleCandidatesResponse>(Error.Conflict(
                "This company is under a legal hold. Candidate purge is suspended until the hold is lifted."));
        }

        var now = clock.UtcNowOffset();
        var settings = await recruitmentSettingsReader.GetRecruitmentSettingsAsync(request.CompanyId, cancellationToken);
        var cutoff = now.AddDays(-settings.CandidateRetentionDays);

        var candidateIdsWithOpenApplications = db.Applications
            .Where(a => a.CompanyId == request.CompanyId && a.WithdrawnAt == null)
            .Join(db.RecruitmentStages.Where(s => !s.IsTerminal),
                a => a.CurrentStageId, s => s.Id, (a, s) => a.CandidateId)
            .Distinct();

        var eligibleCandidates = await db.Candidates
            .Where(c => c.CompanyId == request.CompanyId
                && c.PurgedAt == null
                && c.EmployeeId == null
                && c.UpdatedAt <= cutoff
                && !candidateIdsWithOpenApplications.Contains(c.Id))
            .ToListAsync(cancellationToken);

        if (eligibleCandidates.Count == 0)
            return Result.Success(new PurgeEligibleCandidatesResponse(0));

        var eligibleCandidateIds = eligibleCandidates.Select(c => c.Id).ToList();

        foreach (var candidate in eligibleCandidates)
            candidate.Purge(purgedBy, now);

        // Ticket 7 (P2): purge previously only redacted the Candidate's own basic fields, leaving
        // uploaded documents and free-form application text (recruiter notes, rejection reason,
        // offer notes, CV review notes) fully intact. Widen the purge boundary to cover both:
        //  - Application free text is redacted in place (structural/history fields — stage, dates,
        //    salary — are retained; see Application.RedactPersonalData).
        //  - CandidateDocument rows are hard-deleted here (so DownloadCandidateDocument 404s
        //    immediately, regardless of storage timing); the actual blob deletion is handed off to
        //    PurgeCandidateDocumentStorageJob (durable, Hangfire-retried) so a transient storage
        //    failure never blocks or partially-applies this purge.
        var applicationsToRedact = await db.Applications
            .Where(a => a.CompanyId == request.CompanyId && eligibleCandidateIds.Contains(a.CandidateId))
            .ToListAsync(cancellationToken);

        foreach (var application in applicationsToRedact)
        {
            application.RedactPersonalData(now);
            application.IncrementVersion();
        }

        var documentsToPurge = await db.CandidateDocuments
            .Where(d => d.CompanyId == request.CompanyId && eligibleCandidateIds.Contains(d.CandidateId))
            .ToListAsync(cancellationToken);

        db.CandidateDocuments.RemoveRange(documentsToPurge);

        // Ticket 13 (P2): persist one durable deletion operation per storage key in the SAME
        // transaction that deletes the CandidateDocument rows — previously the storage key existed
        // only in memory (storageKeysToDelete) between this commit and the Hangfire enqueue call
        // below; a crash in that window permanently lost it, since the only other place it lived
        // (the CandidateDocument row) had already been deleted. See
        // Jobs/PurgeCandidateDocumentStorageReconciliationJob.cs for the recovery sweep.
        var deletionOperations = documentsToPurge
            .Select(d => CandidateDocumentDeletionOperation.CreatePending(
                Guid.NewGuid(), request.CompanyId, d.CandidateId, d.StorageKey, now))
            .ToList();
        db.CandidateDocumentDeletionOperations.AddRange(deletionOperations);

        // Ticket 13 (P2): same durability gap as above, for the purge audit event — previously
        // published only after commit, with no record if that publish itself failed.
        var auditDelivery = CandidatePurgeAuditDelivery.CreatePending(
            Guid.NewGuid(), request.CompanyId, eligibleCandidateIds, purgedBy, now);
        db.CandidatePurgeAuditDeliveries.Add(auditDelivery);

        var response = new PurgeEligibleCandidatesResponse(eligibleCandidates.Count);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await db.SaveIdempotentAsync<IdempotencyRecord, PurgeEligibleCandidatesResponse>(
                db.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return Result.Success(outcome.Response!);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        // Latency optimisation only — correctness comes from the persisted operations above plus
        // PurgeCandidateDocumentStorageReconciliationJob's recurring sweep, not from this enqueue
        // succeeding.
        foreach (var operation in deletionOperations)
        {
            backgroundJobClient.Enqueue<PurgeCandidateDocumentStorageJob>(job => job.ProcessAsync(operation.Id));
        }

        try
        {
            await auditPublisher.PublishAsync(
                new CandidatesPurgedAuditEvent(
                    request.CompanyId,
                    eligibleCandidates.Select(c => c.Id).ToList(),
                    purgedBy,
                    now),
                cancellationToken);

            auditDelivery.MarkDelivered(clock.UtcNowOffset());
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            // Left Pending — PurgeCandidateDocumentStorageReconciliationJob's recurring sweep
            // retries delivery; the purge itself is already fully committed and must not be failed
            // back to the caller over an audit-publish fault.
        }

        return Result.Success(response);
    }
}
