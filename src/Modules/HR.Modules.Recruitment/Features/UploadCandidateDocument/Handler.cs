using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Modules.Recruitment.Features.UploadCandidateDocument;

/// <summary>
/// Security/code review finding #3 (superseded — see follow-up below): compensates using an
/// independently-bounded cleanup token, never the request token, and logs a compensation failure with
/// correlation context and a redacted storage key.
///
/// Follow-up review finding: durable intent is now written BEFORE the upload, not only as
/// compensation after a failure is detected. A durable <see cref="CandidateDocumentDeletionOperation"/>
/// row (status Reserved) is persisted for the final storage key before any bytes are uploaded. If the
/// document save succeeds, that row is confirmed in the SAME SaveChangesAsync call. If anything fails
/// afterward — including a process crash, or the DB being unreachable for both the document save AND
/// the compensating delete — the Reserved row already durably exists, so
/// PurgeCandidateDocumentStorageReconciliationJob's intent sweep (see that job's remarks) is the
/// authoritative backstop: after a grace period it checks whether the object actually exists in
/// storage and either hands it to the existing Pending claim/delete pipeline or clears it if nothing
/// was ever uploaded. "Log an unrecoverable orphan and give up" is no longer a terminal outcome here.
/// </summary>
internal sealed class UploadCandidateDocumentHandler(
    RecruitmentDbContext db,
    ICandidateDocumentStorageService storage,
    IOptions<CandidateDocumentUploadOptions> options,
    IClock clock,
    ILogger<UploadCandidateDocumentHandler> logger,
    IExecutionContextAccessor? executionContextAccessor = null)
{
    /// <summary>Bounded independently of the request's own cancellation — a cancelled/timed-out
    /// request must never prevent compensating cleanup of a blob that was already uploaded.</summary>
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(30);

    public async Task<Result<UploadCandidateDocumentResponse>> HandleAsync(
        UploadCandidateDocumentRequest request,
        Guid uploadedBy,
        CancellationToken cancellationToken)
    {
        var candidate = await db.Candidates
            .AsNoTracking()
            .Where(c => c.Id == request.CandidateId && c.CompanyId == request.CompanyId)
            .Select(c => new { c.PurgedAt })
            .SingleOrDefaultAsync(cancellationToken);

        if (candidate is null)
            return Result.Failure<UploadCandidateDocumentResponse>(
                Error.NotFound($"Candidate '{request.CandidateId}' was not found."));

        // Ticket 7 (P2): a purged candidate's documents/personal data were removed by an explicit,
        // separately-authorised retention action — a new upload must never be able to repopulate them.
        if (candidate.PurgedAt is not null)
            return Result.Failure<UploadCandidateDocumentResponse>(
                Error.Conflict("This candidate's data has been purged under the retention policy and can no longer accept new documents."));

        var file = request.File;
        var validationResult = Validate(file.FileName, file.ContentType, file.Length, options.Value);
        if (validationResult.IsFailure)
            return Result.Failure<UploadCandidateDocumentResponse>(validationResult.Error);

        await using var fileStream = file.OpenReadStream();

        // Follow-up review finding: the storage key is reserved and the durable upload-intent row is
        // persisted BEFORE any bytes are uploaded — this is what makes the intent's durability
        // independent of whatever happens afterward (upload failure, session-save failure, process
        // crash). If this initial save itself fails, the request fails cleanly with nothing uploaded
        // yet — there is nothing to compensate for.
        var storageFolder = $"{request.CompanyId}/{request.CandidateId}";
        var storageKey = storage.GenerateStorageKey(storageFolder, file.FileName);
        var reservedAt = clock.UtcNowOffset();
        var intent = CandidateDocumentDeletionOperation.CreateReservedUploadIntent(
            Guid.NewGuid(), request.CompanyId, request.CandidateId, storageKey, reservedAt,
            executionContextAccessor?.Current);
        db.CandidateDocumentDeletionOperations.Add(intent);
        await db.SaveChangesAsync(cancellationToken);

        await storage.UploadAsync(fileStream, storageKey, file.ContentType, cancellationToken);

        var now = clock.UtcNowOffset();

        var kind = Enum.TryParse<CandidateDocumentKind>(request.Kind, ignoreCase: true, out var parsedKind)
            ? parsedKind
            : CandidateDocumentKind.Other;

        var document = CandidateDocument.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.CandidateId,
            request.Title,
            file.FileName,
            file.Length,
            file.ContentType,
            storageKey,
            uploadedBy,
            now,
            kind);

        db.CandidateDocuments.Add(document);
        intent.MarkConfirmed(now);

        try
        {
            // Both the document insert and the intent's confirmation are saved atomically in this
            // one call — if it fails, the intent row (already durably persisted above, before the
            // upload) simply remains unconfirmed, which is exactly what makes it discoverable by the
            // reconciliation sweep without relying on any compensation write succeeding.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // The failed document entity is still tracked as Added — clear the change tracker
            // before any further SaveChangesAsync call on this context, otherwise a subsequent save
            // could silently re-attempt (and this time succeed in) persisting the very document row
            // whose save just failed.
            db.ChangeTracker.Clear();
            await CompensateFailedUploadAsync(request.CompanyId, request.CandidateId, storageKey);
            throw;
        }

        return Result.Success(new UploadCandidateDocumentResponse(
            document.Id,
            document.CompanyId,
            document.CandidateId,
            document.Title,
            document.Kind.ToString(),
            document.FileName,
            document.FileSize,
            document.ContentType,
            document.CreatedAt));
    }

    /// <summary>
    /// Best-effort optimization only — NOT the source of durability. The
    /// <see cref="CandidateDocumentDeletionOperation"/> Reserved intent row for
    /// <paramref name="storageKey"/> was already durably persisted, unconfirmed, before the upload
    /// was attempted (see <see cref="HandleAsync"/>), so even if every step below fails (or the
    /// process crashes before any of it runs), that row alone guarantees
    /// PurgeCandidateDocumentStorageReconciliationJob's intent sweep will eventually resolve the
    /// blob correctly (see that job's remarks). This method only tries to resolve it immediately, to
    /// avoid waiting for the next sweep. Uses an independently-bounded token (never the request's
    /// own, which may already be cancelled) so a cancelled/timed-out request never blocks the
    /// attempt.
    /// </summary>
    private async Task CompensateFailedUploadAsync(Guid companyId, Guid candidateId, string storageKey)
    {
        using var cleanupCts = new CancellationTokenSource(CleanupTimeout);

        try
        {
            await storage.DeleteAsync(storageKey, cleanupCts.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "UploadCandidateDocumentHandler: best-effort immediate delete failed for company {CompanyId}, candidate {CandidateId}, storage key suffix {StorageKeySuffix}, correlation {CorrelationId}. The durable upload-intent record persisted before the upload guarantees the reconciliation sweep will retry it — no orphan is left untracked.",
                companyId, candidateId, RedactStorageKey(storageKey), executionContextAccessor?.Current?.CorrelationId);
            return;
        }

        try
        {
            var now = clock.UtcNowOffset();
            var intent = await db.CandidateDocumentDeletionOperations
                .SingleOrDefaultAsync(o => o.StorageKey == storageKey, CancellationToken.None);

            if (intent is not null && intent.ConfirmedAt is null)
            {
                intent.MarkConfirmed(now);
                await db.SaveChangesAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            // Marking the intent resolved is itself best-effort — if this fails, the row remains
            // unconfirmed, so the reconciliation sweep will simply find the object already gone from
            // storage (DeleteAsync is idempotent) and clear it itself on its next pass. No orphan
            // trail is lost.
            logger.LogWarning(ex,
                "UploadCandidateDocumentHandler: failed to mark the upload-intent record resolved after a successful compensating delete (company {CompanyId}, candidate {CandidateId}, storage key suffix {StorageKeySuffix}). The reconciliation sweep will reconcile it on its next pass.",
                companyId, candidateId, RedactStorageKey(storageKey));
        }
    }

    /// <summary>Storage keys are prefixed with "{companyId}/{candidateId}/..." — never log the full
    /// key. Only the trailing filename/extension segment is retained for diagnostic value.</summary>
    internal static string RedactStorageKey(string storageKey)
    {
        var lastSlash = storageKey.LastIndexOf('/');
        var tail = lastSlash >= 0 ? storageKey[(lastSlash + 1)..] : storageKey;
        return tail.Length <= 12 ? $"***{tail}" : $"***{tail[^12..]}";
    }

    private static Result Validate(string fileName, string contentType, long fileSize, CandidateDocumentUploadOptions options)
    {
        if (fileSize <= 0)
            return Result.Failure(Error.Validation("File must not be empty."));

        if (fileSize > options.MaxFileSizeBytes)
        {
            var maxMb = options.MaxFileSizeBytes / (1024.0 * 1024.0);
            return Result.Failure(Error.Validation($"File size exceeds the maximum allowed size of {maxMb:0.##} MB."));
        }

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension) ||
            !options.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            var allowed = string.Join(", ", options.AllowedExtensions);
            return Result.Failure(Error.Validation($"File type '{extension}' is not allowed. Allowed types: {allowed}."));
        }

        var normalizedContentType = contentType.Split(';')[0].Trim();
        if (!options.AllowedContentTypes.Contains(normalizedContentType, StringComparer.OrdinalIgnoreCase))
        {
            var allowed = string.Join(", ", options.AllowedContentTypes);
            return Result.Failure(Error.Validation($"Content type '{normalizedContentType}' is not allowed. Allowed types: {allowed}."));
        }

        return Result.Success();
    }
}
