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

        var scannedStorageKey = target.StorageKey;
        var now = clock.UtcNowOffset();
        target.MarkScanning(now);
        await db.SaveChangesAsync();

        try
        {
            VirusScanResult scanResult;
            if (virusScanner is NoOpVirusScanService)
            {
                scanResult = await virusScanner.ScanAsync(System.IO.Stream.Null, target.FileName, CancellationToken.None);
            }
            else if (IsProfilePhoto(targetType))
            {
                await using var content = await profilePhotoStorage.OpenReadAsync(scannedStorageKey, CancellationToken.None);
                scanResult = await virusScanner.ScanAsync(content, target.FileName, CancellationToken.None);
            }
            else if (GetStorage(targetType) is ILocalStorageFileReader localReader)
            {
                await using var content = await localReader.OpenLocalReadStreamAsync(target.StorageKey, CancellationToken.None)
                    ?? throw new FileNotFoundException("The uploaded file to scan was not found in local storage.");
                scanResult = await virusScanner.ScanAsync(content, target.FileName, CancellationToken.None);
            }
            else
            {
                var httpClient = httpClientFactory.CreateClient();
                var downloadUrl = await GetDownloadUrlAsync(targetType, target.StorageKey, CancellationToken.None);

                await using var content = await httpClient.GetStreamAsync(downloadUrl, CancellationToken.None);
                scanResult = await virusScanner.ScanAsync(content, target.FileName, CancellationToken.None);
            }

            now = clock.UtcNowOffset();

            if (!await IsStillCurrentAsync(target, scannedStorageKey))
            {
                logger.LogWarning(
                    "ScanUploadedFileJob: {TargetType} {EntityId} was replaced or removed while scanning - discarding stale result.",
                    targetType, entityId);
                return;
            }

            if (scanResult.IsClean)
            {
                var previousStatus = target.ScanStatus.ToString();
                string? promotedFromKey = null;
                if (target is IPromotableStorageFile promotable
                    && IsProfilePhoto(targetType)
                    && ProfilePhotoStorageKeys.IsQuarantine(target.StorageKey))
                {
                    var cleanKey = await profilePhotoStorage.PromoteToCleanAsync(target.StorageKey, CancellationToken.None);
                    promotedFromKey = target.StorageKey;
                    promotable.PromoteStorageKey(cleanKey, now);
                }

                target.MarkScanClean(now);
                await db.SaveChangesAsync();

                if (promotedFromKey is not null)
                {
                    await TryDeleteQuarantineAsync(promotedFromKey, entityId);
                }

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
            var retryCount = context?.GetJobParameter<int?>("RetryCount") ?? 0;
            var isFinalAttempt = retryCount >= MaxAttempts - 1;

            if (isFinalAttempt)
            {
                var previousStatus = target.ScanStatus.ToString();
                var failedNow = clock.UtcNowOffset();

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

            throw;
        }
    }

    private async Task<bool> IsStillCurrentAsync(IScannableFile target, string scannedStorageKey)
    {
        var entry = db.Entry(target);
        await entry.ReloadAsync();
        return entry.State != EntityState.Detached
            && target.StorageKey == scannedStorageKey
            && target.ScanStatus == FileScanStatus.Scanning;
    }

    private static bool IsProfilePhoto(FileScanTargetType targetType) =>
        targetType is FileScanTargetType.EmployeeProfilePhoto or FileScanTargetType.PendingProfilePhoto;

    private async Task TryDeleteQuarantineAsync(string storageKey, Guid entityId)
    {
        try
        {
            await profilePhotoStorage.DeleteAsync(storageKey, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ScanUploadedFileJob: failed to delete quarantined object after promotion for {EntityId}.", entityId);
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

    private object GetStorage(FileScanTargetType targetType) =>
        targetType is FileScanTargetType.EmployeeProfilePhoto or FileScanTargetType.PendingProfilePhoto
            ? profilePhotoStorage
            : documentStorage;

    private Task<Uri> GetDownloadUrlAsync(FileScanTargetType targetType, string storageKey, CancellationToken ct) =>
        targetType is FileScanTargetType.EmployeeProfilePhoto or FileScanTargetType.PendingProfilePhoto
            ? profilePhotoStorage.GetDownloadUrlAsync(storageKey, ct)
            : documentStorage.GetDownloadUrlAsync(storageKey, ct);

    private Task DeleteFromStorageAsync(FileScanTargetType targetType, string storageKey, CancellationToken ct) =>
        targetType is FileScanTargetType.EmployeeProfilePhoto or FileScanTargetType.PendingProfilePhoto
            ? profilePhotoStorage.DeleteAsync(storageKey, ct)
            : documentStorage.DeleteAsync(storageKey, ct);
}
