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
/// Scans one uploaded file. Work is driven by the durable <see cref="FileScanWork"/> row: the job
/// claims it with a lease token and only the current lease holder may record a result, so duplicate,
/// late or stale jobs can never overwrite a newer outcome or turn an infected/failed file clean.
/// </summary>
[AutomaticRetry(Attempts = FileScanWork.MaxAttempts, DelaysInSeconds = new[] { 30, 120, 600 })]
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
    public const int MaxAttempts = FileScanWork.MaxAttempts;

    public async Task ExecuteAsync(
        FileScanTargetType targetType,
        Guid entityId,
        Guid companyId,
        PerformContext? context = null)
    {
        var target = await FileScanTargets.LoadAsync(db, targetType, entityId, CancellationToken.None);
        if (target is null)
        {
            logger.LogWarning(
                "ScanUploadedFileJob: {TargetType} {EntityId} no longer exists — skipping scan.",
                targetType, entityId);
            await RetireWorkForMissingTargetAsync(targetType, entityId);
            return;
        }

        var now = clock.UtcNowOffset();
        var work = await GetOrCreateWorkAsync(targetType, entityId, companyId, target, now);
        if (work is null)
        {
            return;
        }

        if (!work.IsClaimable(now))
        {
            logger.LogInformation(
                "ScanUploadedFileJob: {TargetType} {EntityId} is {State}; skipping duplicate dispatch.",
                targetType, entityId, work.State);
            return;
        }

        if (FileScanTargets.IsFinal(target.ScanStatus))
        {
            await CompleteWorkOnlyAsync(work, now);
            logger.LogInformation(
                "ScanUploadedFileJob: {TargetType} {EntityId} already {Status}; retiring work item.",
                targetType, entityId, target.ScanStatus);
            return;
        }

        if (work.HasExhaustedAttempts)
        {
            await ExhaustAsync(work, target, "scan_attempts_exhausted", targetType, entityId, companyId);
            return;
        }

        var scannedStorageKey = target.StorageKey;
        var expectedVersion = work.Version;
        var leaseToken = work.Claim(now);
        target.MarkScanning(now);

        var claim = await db.SaveChangesWithConcurrencyAsync(
            work, expectedVersion, "The scan work item was claimed by another worker.", CancellationToken.None);
        if (claim.IsFailure)
        {
            logger.LogInformation(
                "ScanUploadedFileJob: {TargetType} {EntityId} was claimed by another worker; skipping.",
                targetType, entityId);
            return;
        }

        string? promotedFromKey = null;
        string? promotedToKey = null;

        try
        {
            VirusScanResult scanResult;
            if (virusScanner is NoOpVirusScanService)
            {
                scanResult = await virusScanner.ScanAsync(Stream.Null, target.FileName, CancellationToken.None);
            }
            else if (IsProfilePhoto(targetType))
            {
                await using var content = await profilePhotoStorage.OpenReadAsync(scannedStorageKey, CancellationToken.None);
                scanResult = await virusScanner.ScanAsync(content, target.FileName, CancellationToken.None);
            }
            else if (documentStorage is ILocalStorageFileReader localReader)
            {
                await using var content = await localReader.OpenLocalReadStreamAsync(scannedStorageKey, CancellationToken.None)
                    ?? throw new FileNotFoundException("The uploaded file to scan was not found in local storage.");
                scanResult = await virusScanner.ScanAsync(content, target.FileName, CancellationToken.None);
            }
            else
            {
                var httpClient = httpClientFactory.CreateClient();
                var downloadUrl = await documentStorage.GetDownloadUrlAsync(scannedStorageKey, CancellationToken.None);

                await using var content = await httpClient.GetStreamAsync(downloadUrl, CancellationToken.None);
                scanResult = await virusScanner.ScanAsync(content, target.FileName, CancellationToken.None);
            }

            now = clock.UtcNowOffset();

            if (!await HoldsCurrentLeaseAsync(work, target, leaseToken, scannedStorageKey))
            {
                logger.LogWarning(
                    "ScanUploadedFileJob: {TargetType} {EntityId} was replaced, removed or reclaimed while scanning — discarding stale result.",
                    targetType, entityId);
                return;
            }

            var previousStatus = target.ScanStatus.ToString();
            var completionVersion = work.Version;

            if (scanResult.IsClean)
            {
                if (target is IPromotableStorageFile promotable
                    && IsProfilePhoto(targetType)
                    && ProfilePhotoStorageKeys.IsQuarantine(target.StorageKey))
                {
                    promotedToKey = await profilePhotoStorage.PromoteToCleanAsync(target.StorageKey, CancellationToken.None);
                    promotedFromKey = target.StorageKey;
                    promotable.PromoteStorageKey(promotedToKey, now);
                }

                target.MarkScanClean(now);
                work.Complete(now);

                var save = await db.SaveChangesWithConcurrencyAsync(
                    work, completionVersion, "The scan work item changed during completion.", CancellationToken.None);
                if (save.IsFailure)
                {
                    await TryDeleteProfilePhotoObjectAsync(promotedToKey, entityId);
                    logger.LogWarning(
                        "ScanUploadedFileJob: {TargetType} {EntityId} lost its lease before the clean result was recorded; discarding.",
                        targetType, entityId);
                    return;
                }

                if (promotedFromKey is not null)
                {
                    await TryDeleteProfilePhotoObjectAsync(promotedFromKey, entityId);
                }

                await auditPublisher.PublishAsync(new FileScanStatusChangedAuditEvent(
                    companyId, targetType.ToString(), entityId, target.EmployeeId,
                    previousStatus, FileScanStatus.Clean.ToString(), null, now), CancellationToken.None);
            }
            else
            {
                var threatName = scanResult.ThreatName ?? "Unknown threat";

                target.MarkScanInfected(threatName, now);
                work.Complete(now);

                var save = await db.SaveChangesWithConcurrencyAsync(
                    work, completionVersion, "The scan work item changed during completion.", CancellationToken.None);
                if (save.IsFailure)
                {
                    logger.LogWarning(
                        "ScanUploadedFileJob: {TargetType} {EntityId} lost its lease before the infected result was recorded; discarding.",
                        targetType, entityId);
                    return;
                }

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
            await HandleFailureAsync(ex, work, target, leaseToken, targetType, entityId, companyId);
            throw;
        }
    }

    private async Task HandleFailureAsync(
        Exception ex,
        FileScanWork work,
        IScannableFile target,
        Guid leaseToken,
        FileScanTargetType targetType,
        Guid entityId,
        Guid companyId)
    {
        try
        {
            await db.Entry(work).ReloadAsync();
            if (db.Entry(work).State == EntityState.Detached || !work.HoldsLease(leaseToken))
            {
                return;
            }

            await db.Entry(target).ReloadAsync();

            var safeFailureReason = VirusScanFailureReasonMapper.ToSafeCategory(ex);
            var now = clock.UtcNowOffset();

            if (work.HasExhaustedAttempts)
            {
                var previousStatus = target.ScanStatus.ToString();
                var expected = work.Version;
                target.MarkScanFailed(safeFailureReason, now);
                work.Exhaust(safeFailureReason, now);
                var save = await db.SaveChangesWithConcurrencyAsync(
                    work, expected, "The scan work item changed while recording failure.", CancellationToken.None);
                if (save.IsFailure)
                {
                    return;
                }

                logger.LogCritical(ex,
                    "ScanUploadedFileJob: virus scan permanently failed after {Attempts} attempts for {TargetType} {EntityId} (Company {CompanyId}).",
                    MaxAttempts, targetType, entityId, companyId);

                await auditPublisher.PublishAsync(new FileScanStatusChangedAuditEvent(
                    companyId, targetType.ToString(), entityId, target.EmployeeId,
                    previousStatus, FileScanStatus.Failed.ToString(), safeFailureReason, now), CancellationToken.None);
                return;
            }

            var releaseVersion = work.Version;
            work.Release(safeFailureReason, now);
            await db.SaveChangesWithConcurrencyAsync(
                work, releaseVersion, "The scan work item changed while releasing its lease.", CancellationToken.None);
        }
        catch (Exception recordingEx)
        {
            logger.LogError(recordingEx,
                "ScanUploadedFileJob: could not record scan failure for {TargetType} {EntityId}; the lease will expire and reconciliation will retry.",
                targetType, entityId);
        }
    }

    private async Task<FileScanWork?> GetOrCreateWorkAsync(
        FileScanTargetType targetType,
        Guid entityId,
        Guid companyId,
        IScannableFile target,
        DateTimeOffset now)
    {
        var work = await db.FileScanWork
            .SingleOrDefaultAsync(w => w.TargetType == targetType && w.EntityId == entityId);
        if (work is not null)
        {
            return work;
        }

        if (FileScanTargets.IsFinal(target.ScanStatus))
        {
            return null;
        }

        work = FileScanWork.Create(targetType, entityId, companyId, now);
        db.FileScanWork.Add(work);
        try
        {
            await db.SaveChangesAsync();
            return work;
        }
        catch (DbUpdateException)
        {
            db.Entry(work).State = EntityState.Detached;
            return await db.FileScanWork
                .SingleOrDefaultAsync(w => w.TargetType == targetType && w.EntityId == entityId);
        }
    }

    private async Task RetireWorkForMissingTargetAsync(FileScanTargetType targetType, Guid entityId)
    {
        var work = await db.FileScanWork
            .SingleOrDefaultAsync(w => w.TargetType == targetType && w.EntityId == entityId);
        if (work is null || work.State == FileScanWorkState.Completed)
        {
            return;
        }

        var expected = work.Version;
        work.Complete(clock.UtcNowOffset());
        await db.SaveChangesWithConcurrencyAsync(
            work, expected, "The scan work item changed while retiring it.", CancellationToken.None);
    }

    private async Task CompleteWorkOnlyAsync(FileScanWork work, DateTimeOffset now)
    {
        var expected = work.Version;
        work.Complete(now);
        await db.SaveChangesWithConcurrencyAsync(
            work, expected, "The scan work item changed while retiring it.", CancellationToken.None);
    }

    private async Task ExhaustAsync(
        FileScanWork work,
        IScannableFile target,
        string safeReason,
        FileScanTargetType targetType,
        Guid entityId,
        Guid companyId)
    {
        var now = clock.UtcNowOffset();
        var previousStatus = target.ScanStatus.ToString();
        var expected = work.Version;

        target.MarkScanFailed(safeReason, now);
        work.Exhaust(safeReason, now);
        var save = await db.SaveChangesWithConcurrencyAsync(
            work, expected, "The scan work item changed while exhausting it.", CancellationToken.None);
        if (save.IsFailure)
        {
            return;
        }

        logger.LogCritical(
            "ScanUploadedFileJob: scan attempts exhausted for {TargetType} {EntityId} (Company {CompanyId}); file marked Failed.",
            targetType, entityId, companyId);

        await auditPublisher.PublishAsync(new FileScanStatusChangedAuditEvent(
            companyId, targetType.ToString(), entityId, target.EmployeeId,
            previousStatus, FileScanStatus.Failed.ToString(), safeReason, now), CancellationToken.None);
    }

    private async Task<bool> HoldsCurrentLeaseAsync(
        FileScanWork work, IScannableFile target, Guid leaseToken, string scannedStorageKey)
    {
        var workEntry = db.Entry(work);
        await workEntry.ReloadAsync();
        if (workEntry.State == EntityState.Detached || !work.HoldsLease(leaseToken))
        {
            return false;
        }

        var targetEntry = db.Entry(target);
        await targetEntry.ReloadAsync();

        return targetEntry.State != EntityState.Detached
            && target.StorageKey == scannedStorageKey
            && target.ScanStatus == FileScanStatus.Scanning;
    }

    private static bool IsProfilePhoto(FileScanTargetType targetType) =>
        targetType is FileScanTargetType.EmployeeProfilePhoto or FileScanTargetType.PendingProfilePhoto;

    private async Task TryDeleteProfilePhotoObjectAsync(string? storageKey, Guid entityId)
    {
        if (storageKey is null)
        {
            return;
        }

        try
        {
            await profilePhotoStorage.DeleteAsync(storageKey, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ScanUploadedFileJob: failed to delete superseded profile photo object for {EntityId}.", entityId);
        }
    }

    private Task DeleteFromStorageAsync(FileScanTargetType targetType, string storageKey, CancellationToken ct) =>
        IsProfilePhoto(targetType)
            ? profilePhotoStorage.DeleteAsync(storageKey, ct)
            : documentStorage.DeleteAsync(storageKey, ct);
}
