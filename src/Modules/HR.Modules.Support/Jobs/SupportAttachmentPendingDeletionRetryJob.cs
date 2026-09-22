using HR.Infrastructure.Abstractions;
using HR.Modules.Support.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Support.Jobs;

/// <summary>
/// Reliability review issue 4 (P1): durable retry/reconciliation for support-attachment storage
/// blobs that failed immediate best-effort deletion during cleanup-on-failure (see
/// UploadedAttachmentCleanupScope). Mirrors the existing retry-until-resolved shape of
/// SupportNotificationRetryJob in this same module.
/// </summary>
internal sealed class SupportAttachmentPendingDeletionRetryJob(
    SupportDbContext db,
    ISupportAttachmentStorageService attachmentStorage,
    IClock clock,
    ILogger<SupportAttachmentPendingDeletionRetryJob> logger)
{
    private const int MaxAttempts = 10;

    public async Task ExecuteAsync()
    {
        var pending = await db.SupportAttachmentPendingDeletions
            .Where(d => d.ResolvedAt == null && d.AttemptCount < MaxAttempts)
            .ToListAsync();

        if (pending.Count == 0)
            return;

        var now = clock.UtcNowOffset();

        foreach (var entry in pending)
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            try
            {
                await attachmentStorage.DeleteAsync(entry.StorageKey, cleanupCts.Token);
                entry.MarkResolved(now);

                logger.LogInformation(
                    "SupportAttachmentPendingDeletionRetryJob: resolved pending deletion for {StorageKey} on attempt {AttemptCount}.",
                    entry.StorageKey, entry.AttemptCount);
            }
            catch (Exception ex)
            {
                entry.RecordRetryFailure(ex.Message, now);

                logger.LogWarning(ex,
                    "SupportAttachmentPendingDeletionRetryJob: attempt {AttemptCount} failed to delete {StorageKey} — will retry on the next sweep.",
                    entry.AttemptCount, entry.StorageKey);
            }
        }

        await db.SaveChangesAsync();
    }
}
