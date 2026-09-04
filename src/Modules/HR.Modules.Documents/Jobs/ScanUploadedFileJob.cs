using Hangfire;
using Hangfire.Server;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Documents.Jobs;

/// <summary>
/// Hangfire enqueue-style (one-off, on-upload) job that virus-scans a single uploaded file and
/// updates its ScanStatus. This is the first enqueue-style job in the Documents module — every
/// other job here (SharedCompanyDocumentAcknowledgementReminderJob, DetectDocumentsDueForReviewJob)
/// is a daily recurring job registered via IRecurringJobManager; this one is queued directly from
/// each upload handler via IBackgroundJobClient.Enqueue, once per upload, immediately after the
/// row is persisted as Pending.
///
/// A single job class (rather than five) covers all five scannable entity kinds via
/// <see cref="FileScanTargetType"/> and the shared <see cref="IScannableFile"/> shape — the
/// scanner itself (<see cref="IVirusScanService"/>) has no knowledge of which kind of row it's
/// scanning, only a stream and a file name, so there is no scanner-specific coupling here either.
///
/// Retry behaviour: [AutomaticRetry] lets Hangfire retry a scanner-unreachable/errored failure
/// automatically. On the final exhausted attempt the entity is marked Failed, a structured
/// critical log is written, and the exception is still rethrown so the existing
/// BackgroundJobAuditFilter (HR.Infrastructure.BackgroundJobs) writes its own
/// BackgroundJobFailedAuditEvent — the operational alert path this app already has, rather than a
/// new one invented for this feature.
/// </summary>
[AutomaticRetry(Attempts = MaxAttempts, DelaysInSeconds = new[] { 30, 120, 600 })]
internal sealed class ScanUploadedFileJob(
    DocumentsDbContext db,
    IDocumentStorageService documentStorage,
    IProfilePhotoStorageService profilePhotoStorage,
    IVirusScanService virusScanner,
    IHttpClientFactory httpClientFactory,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    ILogger<ScanUploadedFileJob> logger)
{
    public const int MaxAttempts = 5;

    public async Task ExecuteAsync(
        FileScanTargetType targetType,
        Guid entityId,
        Guid companyId,
        PerformContext? context = null)
    {
        var target = await LoadTargetAsync(targetType, entityId, CancellationToken.None);
        if (target is null)
        {
            logger.LogWarning(
                "ScanUploadedFileJob: {TargetType} {EntityId} no longer exists — skipping scan.",
                targetType, entityId);
            return;
        }

        var now = clock.UtcNowOffset();
        target.MarkScanning(now);
        await db.SaveChangesAsync();

        try
        {
            var httpClient = httpClientFactory.CreateClient();
            var downloadUrl = await GetDownloadUrlAsync(targetType, target.StorageKey, CancellationToken.None);

            await using var content = await httpClient.GetStreamAsync(downloadUrl, CancellationToken.None);
            var scanResult = await virusScanner.ScanAsync(content, target.FileName, CancellationToken.None);

            now = clock.UtcNowOffset();

            if (scanResult.IsClean)
            {
                var previousStatus = target.ScanStatus.ToString();
                target.MarkScanClean(now);
                await db.SaveChangesAsync();

                await auditPublisher.PublishAsync(new FileScanStatusChangedAuditEvent(
                    companyId, targetType.ToString(), entityId, target.EmployeeId,
                    previousStatus, FileScanStatus.Clean.ToString(), null, now), CancellationToken.None);
            }
            else
            {
                var previousStatus = target.ScanStatus.ToString();
                var threatName = scanResult.ThreatName ?? "Unknown threat";

                target.MarkScanInfected(threatName, now);
                await db.SaveChangesAsync();

                // Infected files are removed from storage immediately — the entity row is kept
                // (marked Infected) purely as a record; ScanStatusAccessGuard makes sure nobody
                // can download it, and the underlying blob is gone so there is nothing to leak.
                try
                {
                    await DeleteFromStorageAsync(targetType, target.StorageKey, CancellationToken.None);
                }
                catch (Exception deleteEx)
                {
                    logger.LogError(deleteEx,
                        "ScanUploadedFileJob: failed to delete infected file from storage for {TargetType} {EntityId}.",
                        targetType, entityId);
                }

                logger.LogWarning(
                    "Virus scan detected an infected file: {TargetType} {EntityId} (Company {CompanyId}), threat '{ThreatName}'.",
                    targetType, entityId, companyId, threatName);

                await auditPublisher.PublishAsync(new FileScanStatusChangedAuditEvent(
                    companyId, targetType.ToString(), entityId, target.EmployeeId,
                    previousStatus, FileScanStatus.Infected.ToString(), threatName, now), CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            // Scanner unreachable/errored. Let Hangfire's automatic retry handle it — only mark
            // the entity Failed once this was the final attempt.
            var retryCount = context?.GetJobParameter<int?>("RetryCount") ?? 0;
            var isFinalAttempt = retryCount >= MaxAttempts - 1;

            if (isFinalAttempt)
            {
                var previousStatus = target.ScanStatus.ToString();
                var failedNow = clock.UtcNowOffset();

                // Only a safe, closed-set category is ever persisted/audited — the raw exception
                // (which can carry internal paths, hosts, storage addresses, signed URLs, tokens or
                // personal data) is logged in full below via ILogger for restricted operational
                // diagnosis only. See VirusScanFailureReasonMapper for the mapping rules.
                var safeFailureReason = VirusScanFailureReasonMapper.ToSafeCategory(ex);

                target.MarkScanFailed(safeFailureReason, failedNow);
                await db.SaveChangesAsync();

                logger.LogCritical(ex,
                    "ScanUploadedFileJob: virus scan permanently failed after {Attempts} attempts for {TargetType} {EntityId} (Company {CompanyId}).",
                    MaxAttempts, targetType, entityId, companyId);

                await auditPublisher.PublishAsync(new FileScanStatusChangedAuditEvent(
                    companyId, targetType.ToString(), entityId, target.EmployeeId,
                    previousStatus, FileScanStatus.Failed.ToString(), safeFailureReason, failedNow), CancellationToken.None);
            }

            // Rethrow in all cases: while retries remain, this is what triggers Hangfire's retry;
            // on the final attempt it's what lets BackgroundJobAuditFilter record the standard
            // operational-failure audit trail this app already relies on for every other job.
            throw;
        }
    }

    private Task<IScannableFile?> LoadTargetAsync(
        FileScanTargetType targetType, Guid entityId, CancellationToken cancellationToken) => targetType switch
    {
        FileScanTargetType.Document =>
            FindAsync(db.Documents, entityId, cancellationToken),
        FileScanTargetType.EmployeeProfilePhoto =>
            FindAsync(db.EmployeeProfilePhotos, entityId, cancellationToken),
        FileScanTargetType.PendingProfilePhoto =>
            FindAsync(db.PendingProfilePhotos, entityId, cancellationToken),
        FileScanTargetType.SharedCompanyDocument =>
            FindAsync(db.SharedCompanyDocuments, entityId, cancellationToken),
        FileScanTargetType.SharedCompanyDocumentVersion =>
            FindAsync(db.SharedCompanyDocumentVersions, entityId, cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(targetType), targetType, null),
    };

    private static async Task<IScannableFile?> FindAsync<TEntity>(
        DbSet<TEntity> set, Guid id, CancellationToken cancellationToken)
        where TEntity : class
    {
        var entity = await set.FindAsync([id], cancellationToken);
        return entity as IScannableFile;
    }

    private Task<Uri> GetDownloadUrlAsync(FileScanTargetType targetType, string storageKey, CancellationToken ct) =>
        targetType is FileScanTargetType.EmployeeProfilePhoto or FileScanTargetType.PendingProfilePhoto
            ? profilePhotoStorage.GetDownloadUrlAsync(storageKey, ct)
            : documentStorage.GetDownloadUrlAsync(storageKey, ct);

    private Task DeleteFromStorageAsync(FileScanTargetType targetType, string storageKey, CancellationToken ct) =>
        targetType is FileScanTargetType.EmployeeProfilePhoto or FileScanTargetType.PendingProfilePhoto
            ? profilePhotoStorage.DeleteAsync(storageKey, ct)
            : documentStorage.DeleteAsync(storageKey, ct);
}
