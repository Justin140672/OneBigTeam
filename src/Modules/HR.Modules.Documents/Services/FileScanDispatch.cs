using Hangfire;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Jobs;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Documents.Services;

internal static class FileScanDispatch
{
    /// <summary>
    /// Stages the durable scan work item on the caller's context so it commits in the same
    /// transaction as the upload it belongs to.
    /// </summary>
    public static async Task StageAsync(
        DocumentsDbContext db,
        FileScanTargetType targetType,
        Guid entityId,
        Guid companyId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await db.FileScanWork
            .SingleOrDefaultAsync(w => w.TargetType == targetType && w.EntityId == entityId, cancellationToken);

        if (existing is null)
        {
            db.FileScanWork.Add(FileScanWork.Create(targetType, entityId, companyId, now));
        }
        else
        {
            existing.Restage(now);
        }
    }

    /// <summary>
    /// Post-commit enqueue. The upload is already persisted together with its work item, so an
    /// enqueue failure is logged and left to the reconciler; it must never surface as a failed upload.
    /// </summary>
    public static void TryEnqueue(
        IBackgroundJobClient backgroundJobClient,
        ILogger? logger,
        FileScanTargetType targetType,
        Guid entityId,
        Guid companyId)
    {
        try
        {
            backgroundJobClient.Enqueue<ScanUploadedFileJob>(job =>
                job.ExecuteAsync(targetType, entityId, companyId, null));
        }
        catch (Exception ex)
        {
            logger?.LogError(
                ex,
                "Could not enqueue malware scan for {TargetType} {EntityId} (company {CompanyId}); the persisted scan work item will be redispatched by reconciliation.",
                targetType, entityId, companyId);
        }
    }
}
