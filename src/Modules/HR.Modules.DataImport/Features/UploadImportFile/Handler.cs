using ClosedXML.Excel;
using HR.Modules.DataImport.Domain;
using HR.Modules.DataImport.Persistence;
using HR.Modules.DataImport.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.DataImport.Features.UploadImportFile;

/// <summary>
/// Follow-up review finding: a durable "upload intent" (<see cref="Domain.OrphanedImportFileUpload"/>)
/// is now persisted BEFORE the blob is uploaded, not only after a failure is detected as
/// compensation. The previous implementation (security/code review finding #3) only wrote a durable
/// record once a failure was already observed — if the DB was down AND the best-effort compensating
/// delete/record-write also failed (or the process crashed right after upload), the blob was
/// orphaned with zero durable trace. Because the intent row now exists before the storage upload
/// call is even made, that failure mode is closed: the durable trail exists no matter what fails
/// afterward or when the process dies. See Domain/OrphanedImportFileUpload.cs for the full
/// lifecycle and Jobs/PurgeOrphanedImportFileUploadsJob.cs for the reconciliation sweep that
/// resolves any intent left unconfirmed.
/// </summary>
internal sealed class UploadImportFileHandler(
    DataImportDbContext db,
    IImportFileStorageService storage,
    IImportFileValidator fileValidator,
    IClock clock,
    ILogger<UploadImportFileHandler> logger)
{
    /// <summary>Bounded independently of the request's own cancellation — a cancelled/timed-out
    /// request must never prevent compensating cleanup of a blob that was already uploaded.</summary>
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(30);

    public async Task<Result<UploadImportFileResponse>> HandleAsync(
        UploadImportFileRequest request,
        Guid initiatedByUserId,
        CancellationToken cancellationToken)
    {
        var file = request.File;

        var validationResult = fileValidator.Validate(file.FileName, file.ContentType, file.Length);
        if (validationResult.IsFailure)
            return Result.Failure<UploadImportFileResponse>(validationResult.Error);

        await using var fileStream = file.OpenReadStream();

        // Verify file content matches the declared content type (prevents extension/MIME spoofing).
        var contentResult = fileValidator.ValidateContent(fileStream, file.ContentType);
        if (contentResult.IsFailure)
            return Result.Failure<UploadImportFileResponse>(contentResult.Error);

        fileStream.Seek(0, SeekOrigin.Begin);

        int totalRows;
        try
        {
            totalRows = CountXlsxDataRows(fileStream);
        }
        catch (Exception ex)
        {
            return Result.Failure<UploadImportFileResponse>(
                Error.Validation($"The file could not be read: {ex.Message}"));
        }

        if (totalRows == 0)
        {
            return Result.Failure<UploadImportFileResponse>(
                Error.Validation("The uploaded file has no data rows to import. Add at least one employee row below the header and try again."));
        }

        fileStream.Seek(0, SeekOrigin.Begin);

        // Follow-up review finding: the storage key is reserved and the durable upload-intent row
        // is persisted BEFORE any bytes are uploaded — this is what makes the intent's durability
        // independent of whatever happens afterward (upload failure, session-save failure, process
        // crash). If this initial save itself fails, the request fails cleanly with nothing
        // uploaded yet — there is nothing to compensate for.
        var storageKey = storage.GenerateStorageKey($"{request.CompanyId}", file.FileName);
        var reservedAt = clock.UtcNowOffset();
        var intent = OrphanedImportFileUpload.CreateReserved(
            Guid.NewGuid(), request.CompanyId, storageKey, reservedAt);
        db.OrphanedImportFileUploads.Add(intent);
        await db.SaveChangesAsync(cancellationToken);

        await storage.UploadAsync(fileStream, storageKey, file.ContentType, cancellationToken);

        var now = clock.UtcNowOffset();

        var session = ImportSession.Create(
            Guid.NewGuid(),
            request.CompanyId,
            request.EntityType,
            file.FileName,
            totalRows,
            initiatedByUserId,
            storageKey,
            file.ContentType,
            now);

        db.ImportSessions.Add(session);
        intent.MarkConfirmed(now);

        try
        {
            // Both the session insert and the intent's confirmation are saved atomically in this
            // one call — if it fails, the intent row (already durably persisted above, before the
            // upload) simply remains unconfirmed, which is exactly what makes it discoverable by
            // the reconciliation sweep without relying on any compensation write succeeding.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // The failed session/intent-confirmation entities are still tracked — clear the change
            // tracker before any further SaveChangesAsync call on this context, otherwise a
            // subsequent save could silently re-attempt (and this time succeed in) persisting the
            // very rows whose save just failed.
            db.ChangeTracker.Clear();
            await CompensateFailedUploadAsync(request.CompanyId, storageKey);
            throw;
        }

        return Result.Success(new UploadImportFileResponse(
            session.Id,
            session.CompanyId,
            session.EntityType,
            session.FileName,
            session.Status.ToString(),
            session.TotalRows,
            session.CreatedAt));
    }

    /// <summary>
    /// Best-effort optimization only — NOT the source of durability. The
    /// <see cref="OrphanedImportFileUpload"/> intent row for <paramref name="storageKey"/>
    /// was already durably persisted, unconfirmed, before the upload was attempted (see
    /// <see cref="HandleAsync"/>), so even if every step below fails (or the process crashes before
    /// any of it runs), that row alone guarantees Jobs/PurgeOrphanedImportFileUploadsJob's
    /// reconciliation sweep will eventually resolve the blob correctly. This method only tries to
    /// resolve it immediately, to avoid waiting for the next sweep. Uses an independently-bounded
    /// token (never the request's own, which may already be cancelled) so a cancelled/timed-out
    /// request never blocks the attempt.
    /// </summary>
    private async Task CompensateFailedUploadAsync(Guid companyId, string storageKey)
    {
        using var cleanupCts = new CancellationTokenSource(CleanupTimeout);

        try
        {
            await storage.DeleteAsync(storageKey, cleanupCts.Token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "UploadImportFileHandler: best-effort immediate delete failed for company {CompanyId}, storage key suffix {StorageKeySuffix}. The durable upload-intent record persisted before the upload guarantees the reconciliation sweep will retry it — no orphan is left untracked.",
                companyId, RedactStorageKey(storageKey));
            return;
        }

        try
        {
            var now = clock.UtcNowOffset();
            var intent = await db.OrphanedImportFileUploads
                .SingleOrDefaultAsync(o => o.StorageKey == storageKey, CancellationToken.None);

            if (intent is not null && intent.DeletedAt is null)
            {
                intent.MarkDeleted(now);
                await db.SaveChangesAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            // Marking the intent as resolved is itself best-effort — if this fails, the row
            // remains unconfirmed and undeleted, so the reconciliation sweep will simply find the
            // object already gone from storage (DeleteAsync is idempotent) and mark it deleted
            // itself on its next pass. No orphan trail is lost.
            logger.LogWarning(ex,
                "UploadImportFileHandler: failed to mark the upload-intent record resolved after a successful compensating delete (company {CompanyId}, storage key suffix {StorageKeySuffix}). The reconciliation sweep will reconcile it on its next pass.",
                companyId, RedactStorageKey(storageKey));
        }
    }

    /// <summary>Storage keys are prefixed with "{companyId}/..." — never log the full key. Only
    /// the trailing filename/extension segment is retained for diagnostic value.</summary>
    internal static string RedactStorageKey(string storageKey)
    {
        var lastSlash = storageKey.LastIndexOf('/');
        var tail = lastSlash >= 0 ? storageKey[(lastSlash + 1)..] : storageKey;
        return tail.Length <= 12 ? $"***{tail}" : $"***{tail[^12..]}";
    }

    // Determines the workbook's data row count (excluding the header row).
    private static int CountXlsxDataRows(Stream content)
    {
        using var workbook = new XLWorkbook(content);
        var worksheet = workbook.Worksheet(1);
        var rowCount = worksheet.RangeUsed()?.RowCount() ?? 0;
        return Math.Max(0, rowCount - 1); // exclude header row
    }
}
