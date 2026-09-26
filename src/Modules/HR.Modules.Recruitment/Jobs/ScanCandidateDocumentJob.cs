using Hangfire;
using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Recruitment.Jobs;

/// <summary>
/// [P1] Malware-scans one uploaded candidate document and records the outcome on the
/// <see cref="CandidateDocument"/> row. Mirrors HR.Modules.Documents' ScanUploadedFileJob (enqueue-
/// style, one job per upload, shared <see cref="IUploadedFileScanner"/> contract, closed-set failure
/// reasons, infected blobs removed from storage, row retained as evidence), with three deliberate
/// hardening differences:
/// <list type="bullet">
/// <item><description>The bytes are read server-side via
/// <see cref="ICandidateDocumentStorageService.OpenReadAsync"/> — a signed download URL is never
/// minted for unscanned content, not even internally.</description></item>
/// <item><description>Attempts are bounded by the persisted <see cref="CandidateDocument.ScanAttemptCount"/>
/// (not Hangfire's per-job retry counter), so this job, its own scheduled retries and
/// <see cref="ReconcileCandidateDocumentScansJob"/> share one budget of
/// <see cref="CandidateDocument.MaxScanAttempts"/>. Claims use that count as an optimistic-concurrency
/// token, so duplicate dispatches of the same document cannot both scan it.</description></item>
/// <item><description>Infected blobs are deleted through the module's durable
/// <see cref="CandidateDocumentDeletionOperation"/> pipeline (persisted in the same save as the
/// Infected status; retried by PurgeCandidateDocumentStorageJob and its reconciliation sweep), not a
/// fire-and-forget delete.</description></item>
/// </list>
/// A scanner or storage outage is never treated as clean: it is recorded as a failed attempt,
/// audited, and retried with back-off until the budget is spent, after which the document is
/// terminally Failed (never downloadable) and the job fails visibly in Hangfire.
/// </summary>
[AutomaticRetry(Attempts = 0)]
internal sealed class ScanCandidateDocumentJob(
    RecruitmentDbContext db,
    ICandidateDocumentStorageService storage,
    IUploadedFileScanner scanner,
    IBackgroundJobClient backgroundJobClient,
    IAuditEventPublisher auditPublisher,
    IClock clock,
    ILogger<ScanCandidateDocumentJob> logger)
{
    /// <summary>Back-off before the attempt that follows failed attempt N (index N-1).</summary>
    internal static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
    ];

    /// <summary>Upper bound on reading + scanning one document (the scanner has its own, shorter timeout).</summary>
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromMinutes(5);

    internal static TimeSpan RetryDelayAfter(int failedAttempt) =>
        RetryDelays[Math.Clamp(failedAttempt - 1, 0, RetryDelays.Length - 1)];

    public async Task ScanAsync(Guid documentId)
    {
        var document = await db.CandidateDocuments.SingleOrDefaultAsync(d => d.Id == documentId);
        if (document is null)
        {
            // Deleted/purged since dispatch, or dispatched for a save that was rolled back.
            logger.LogInformation(
                "ScanCandidateDocumentJob: candidate document {DocumentId} no longer exists — nothing to scan.",
                documentId);
            return;
        }

        var now = clock.UtcNowOffset();

        if (document.IsScanTerminal || !document.CanBeginScanAttempt(now))
        {
            // Already scanned, still in back-off, or another worker holds a live claim.
            return;
        }

        if (!document.HasRemainingScanAttempts)
        {
            await CloseOffExhaustedAsync(document, now);
            return;
        }

        var previousStatus = document.ScanStatus;
        document.BeginScanAttempt(now);
        if (!await TrySaveAsync(document, "claim"))
            return;

        UploadedFileScanResult result;
        try
        {
            using var timeout = new CancellationTokenSource(AttemptTimeout);
            await using var content = await storage.OpenReadAsync(document.StorageKey, timeout.Token);
            result = await scanner.ScanAsync(content, document.FileName, timeout.Token);
        }
        catch (Exception ex)
        {
            await RecordFailedAttemptAsync(document, ex);
            return;
        }

        if (result.IsClean)
            await RecordCleanAsync(document, previousStatus);
        else
            await QuarantineAsync(document, result.ThreatName);
    }

    private async Task RecordCleanAsync(CandidateDocument document, CandidateDocumentScanStatus previousStatus)
    {
        var now = clock.UtcNowOffset();
        document.MarkScanClean(now);
        if (!await TrySaveAsync(document, "clean result"))
            return;

        logger.LogInformation(
            "ScanCandidateDocumentJob: candidate document {DocumentId} (company {CompanyId}) scanned clean on attempt {Attempt}.",
            document.Id, document.CompanyId, document.ScanAttemptCount);

        await PublishAuditAsync(new CandidateDocumentScanStatusChangedAuditEvent(
            document.CompanyId, document.Id, document.CandidateId,
            previousStatus.ToString(), CandidateDocumentScanStatus.Clean.ToString(),
            document.ScanAttemptCount, null, now));
    }

    private async Task QuarantineAsync(CandidateDocument document, string? threatName)
    {
        var now = clock.UtcNowOffset();
        document.MarkScanInfected(threatName, now);

        // Durable quarantine: the deletion intent for the blob is committed atomically with the
        // Infected status, so a crash after this save can never leave an infected blob untracked.
        var deletion = CandidateDocumentDeletionOperation.CreatePending(
            Guid.NewGuid(), document.CompanyId, document.CandidateId, document.StorageKey, now);
        db.CandidateDocumentDeletionOperations.Add(deletion);

        if (!await TrySaveAsync(document, "infected result"))
            return;

        logger.LogWarning(
            "ScanCandidateDocumentJob: malware detected in candidate document {DocumentId} (candidate {CandidateId}, company {CompanyId}), threat '{ThreatName}'. Quarantined; deletion operation {OperationId}.",
            document.Id, document.CandidateId, document.CompanyId, document.ScanFailureReason, deletion.Id);

        await PublishAuditAsync(new CandidateDocumentScanStatusChangedAuditEvent(
            document.CompanyId, document.Id, document.CandidateId,
            CandidateDocumentScanStatus.Scanning.ToString(), CandidateDocumentScanStatus.Infected.ToString(),
            document.ScanAttemptCount, document.ScanFailureReason, now));

        await PublishAuditAsync(new CandidateDocumentQuarantinedAuditEvent(
            document.CompanyId, document.Id, document.CandidateId, deletion.Id,
            document.ScanFailureReason ?? CandidateDocumentScanFailureReasons.UnknownThreat,
            document.ScanAttemptCount, now));

        // Latency optimisation only — the durable operation row is the guarantee. The existing
        // worker applies the legal-hold check, claim/lease and bounded retries.
        try
        {
            backgroundJobClient.Enqueue<PurgeCandidateDocumentStorageJob>(job => job.ProcessAsync(deletion.Id));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "ScanCandidateDocumentJob: could not enqueue quarantine deletion {OperationId} for document {DocumentId}; PurgeCandidateDocumentStorageReconciliationJob will pick it up.",
                deletion.Id, document.Id);
        }
    }

    private async Task RecordFailedAttemptAsync(CandidateDocument document, Exception exception)
    {
        var now = clock.UtcNowOffset();
        var reason = CandidateDocumentScanFailureReasons.FromException(exception);
        var attempt = document.ScanAttemptCount;
        var nextAttemptAt = now + RetryDelayAfter(attempt);

        document.RecordFailedScanAttempt(reason, now, nextAttemptAt);
        if (!await TrySaveAsync(document, "failed attempt"))
            return;

        if (document.ScanStatus == CandidateDocumentScanStatus.Failed)
        {
            logger.LogCritical(exception,
                "ScanCandidateDocumentJob: malware scan permanently failed for candidate document {DocumentId} (company {CompanyId}) after {Attempts} attempts. The document is blocked from download.",
                document.Id, document.CompanyId, attempt);

            await PublishAuditAsync(new CandidateDocumentScanStatusChangedAuditEvent(
                document.CompanyId, document.Id, document.CandidateId,
                CandidateDocumentScanStatus.Scanning.ToString(), CandidateDocumentScanStatus.Failed.ToString(),
                attempt, reason, now));

            // Fail the Hangfire job so the permanent failure is visible on the dashboard,
            // /health/background-jobs and the BackgroundJobAuditFilter trail.
            throw new CandidateDocumentScanFailedException(document.Id, attempt, exception);
        }

        logger.LogWarning(exception,
            "ScanCandidateDocumentJob: malware scan attempt {Attempt} of {MaxAttempts} failed for candidate document {DocumentId} (company {CompanyId}); retrying at {NextAttemptAt}.",
            attempt, CandidateDocument.MaxScanAttempts, document.Id, document.CompanyId, nextAttemptAt);

        await PublishAuditAsync(new CandidateDocumentScanRetryScheduledAuditEvent(
            document.CompanyId, document.Id, document.CandidateId,
            attempt, CandidateDocument.MaxScanAttempts, reason, nextAttemptAt, now));

        // Latency optimisation only — the persisted ScanNextAttemptAt makes the retry durable via
        // ReconcileCandidateDocumentScansJob even if this schedule call is lost.
        try
        {
            backgroundJobClient.Schedule<ScanCandidateDocumentJob>(
                job => job.ScanAsync(document.Id), nextAttemptAt - now);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "ScanCandidateDocumentJob: could not schedule scan retry for candidate document {DocumentId}; the reconciliation sweep will retry it.",
                document.Id);
        }
    }

    private async Task CloseOffExhaustedAsync(CandidateDocument document, DateTimeOffset now)
    {
        var previous = document.ScanStatus;
        document.MarkScanRetryLimitReached(now);
        if (!await TrySaveAsync(document, "retry-limit close-off"))
            return;

        logger.LogCritical(
            "ScanCandidateDocumentJob: candidate document {DocumentId} (company {CompanyId}) exhausted its {MaxAttempts} scan attempts and is blocked from download.",
            document.Id, document.CompanyId, CandidateDocument.MaxScanAttempts);

        await PublishAuditAsync(new CandidateDocumentScanStatusChangedAuditEvent(
            document.CompanyId, document.Id, document.CandidateId,
            previous.ToString(), CandidateDocumentScanStatus.Failed.ToString(),
            document.ScanAttemptCount, document.ScanFailureReason, now));
    }

    /// <summary>False when another worker changed the row first (lost claim / stale attempt): this
    /// worker's result is discarded and the change tracker is reset.</summary>
    private async Task<bool> TrySaveAsync(CandidateDocument document, string step)
    {
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            logger.LogInformation(
                "ScanCandidateDocumentJob: candidate document {DocumentId} was updated by another scan worker during the {Step} step — discarding this attempt's write.",
                document.Id, step);
            return false;
        }
    }

    private async Task PublishAuditAsync<TEvent>(TEvent auditEvent) where TEvent : IAuditEvent
    {
        try
        {
            await auditPublisher.PublishAsync(auditEvent, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Audit must never undo or block a recorded scan outcome.
            logger.LogError(ex, "ScanCandidateDocumentJob: failed to publish audit event {EventType}.", typeof(TEvent).Name);
        }
    }
}

/// <summary>Raised (after the Failed state is persisted and audited) so Hangfire records the job as failed.</summary>
internal sealed class CandidateDocumentScanFailedException(Guid documentId, int attempts, Exception inner)
    : Exception($"Malware scan for candidate document {documentId} permanently failed after {attempts} attempts.", inner);
