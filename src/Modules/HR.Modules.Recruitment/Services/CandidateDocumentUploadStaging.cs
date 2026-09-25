using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Recruitment.Services;

/// <summary>
/// A candidate document blob that has been uploaded under a durable, already-persisted Reserved
/// upload intent (<see cref="CandidateDocumentDeletionOperation"/>). The caller must either confirm
/// <see cref="Intent"/> in the same SaveChangesAsync call that inserts the owning
/// <see cref="CandidateDocument"/>, or call <see cref="CandidateDocumentUploadStaging.CompensateAsync"/>.
/// </summary>
internal sealed record StagedCandidateDocumentUpload(
    Guid CompanyId,
    Guid CandidateId,
    string StorageKey,
    string FileName,
    long FileSize,
    string ContentType,
    CandidateDocumentDeletionOperation Intent);

/// <summary>
/// The crash-safe candidate document upload sequence, shared by UploadCandidateDocument and the
/// coordinated CreateCandidateApplication intake (internal recruitment Ticket 3) so the durability
/// guarantees are implemented once:
/// <list type="number">
/// <item><description>A Reserved upload intent naming the final storage key is persisted BEFORE any
/// bytes are uploaded (<see cref="ReserveAndUploadAsync"/>).</description></item>
/// <item><description>The caller confirms the intent in the SAME SaveChangesAsync call that inserts the
/// document row, so a failed or rolled-back save leaves the intent unconfirmed.</description></item>
/// <item><description>On failure the caller clears its change tracker and calls
/// <see cref="CompensateAsync"/> for a best-effort immediate delete. Even if that fails (or the process
/// crashes first), PurgeCandidateDocumentStorageReconciliationJob's intent sweep resolves the
/// unconfirmed intent, so a blob is never left untracked.</description></item>
/// </list>
/// </summary>
internal sealed class CandidateDocumentUploadStaging(
    RecruitmentDbContext db,
    ICandidateDocumentStorageService storage,
    IClock clock,
    ILogger logger,
    IExecutionContextAccessor? executionContextAccessor = null)
{
    /// <summary>Bounded independently of the request's own cancellation — a cancelled/timed-out
    /// request must never prevent compensating cleanup of a blob that was already uploaded.</summary>
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Persists a Reserved upload intent for a freshly generated storage key (its own
    /// SaveChangesAsync — callers must not have other pending changes tracked), then uploads the file.
    /// If the intent save fails nothing has been uploaded. If the upload itself fails the intent
    /// remains Reserved and the reconciliation sweep resolves it.
    /// </summary>
    public async Task<StagedCandidateDocumentUpload> ReserveAndUploadAsync(
        Guid companyId,
        Guid candidateId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var fileStream = file.OpenReadStream();

        var storageFolder = $"{companyId}/{candidateId}";
        var storageKey = storage.GenerateStorageKey(storageFolder, file.FileName);
        var intent = CandidateDocumentDeletionOperation.CreateReservedUploadIntent(
            Guid.NewGuid(), companyId, candidateId, storageKey, clock.UtcNowOffset(),
            executionContextAccessor?.Current);
        db.CandidateDocumentDeletionOperations.Add(intent);
        await db.SaveChangesAsync(cancellationToken);

        await storage.UploadAsync(fileStream, storageKey, file.ContentType, cancellationToken);

        return new StagedCandidateDocumentUpload(
            companyId, candidateId, storageKey, file.FileName, file.Length, file.ContentType, intent);
    }

    /// <summary>
    /// Best-effort optimisation only — NOT the source of durability (the Reserved intent persisted
    /// before the upload is). Deletes the uploaded blob and marks the intent resolved. Callers must
    /// clear the change tracker (and dispose any open transaction) first, so this method's own save
    /// cannot re-attempt persisting whatever failed. Uses an independently-bounded token, never the
    /// request's own, which may already be cancelled.
    /// </summary>
    public async Task CompensateAsync(StagedCandidateDocumentUpload staged)
    {
        using var cleanupCts = new CancellationTokenSource(CleanupTimeout);

        try
        {
            await storage.DeleteAsync(staged.StorageKey, cleanupCts.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Candidate document upload: best-effort immediate delete failed for company {CompanyId}, candidate {CandidateId}, storage key suffix {StorageKeySuffix}, correlation {CorrelationId}. The durable upload-intent record persisted before the upload guarantees the reconciliation sweep will retry it — no orphan is left untracked.",
                staged.CompanyId, staged.CandidateId, RedactStorageKey(staged.StorageKey), executionContextAccessor?.Current?.CorrelationId);
            return;
        }

        try
        {
            var now = clock.UtcNowOffset();
            var intent = await db.CandidateDocumentDeletionOperations
                .SingleOrDefaultAsync(o => o.StorageKey == staged.StorageKey, CancellationToken.None);

            if (intent is not null && intent.ConfirmedAt is null)
            {
                intent.MarkConfirmed(now);
                await db.SaveChangesAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            // Marking the intent resolved is itself best-effort — if this fails, the row remains
            // unconfirmed, so the reconciliation sweep will find the object already gone from
            // storage (DeleteAsync is idempotent) and clear it on its next pass.
            logger.LogWarning(ex,
                "Candidate document upload: failed to mark the upload-intent record resolved after a successful compensating delete (company {CompanyId}, candidate {CandidateId}, storage key suffix {StorageKeySuffix}). The reconciliation sweep will reconcile it on its next pass.",
                staged.CompanyId, staged.CandidateId, RedactStorageKey(staged.StorageKey));
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

    /// <summary>Size, extension and content-type rules for any candidate document upload.</summary>
    internal static Result ValidateFile(string fileName, string contentType, long fileSize, CandidateDocumentUploadOptions options)
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
