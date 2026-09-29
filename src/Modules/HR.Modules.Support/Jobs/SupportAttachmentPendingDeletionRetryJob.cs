using HR.Infrastructure.Abstractions;
using HR.Modules.Support.Persistence;
using HR.Modules.Support.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Support.Jobs;

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
                    UploadedAttachmentCleanupScope.RedactStorageKey(entry.StorageKey), entry.AttemptCount);
            }
            catch (Exception ex)
            {
                entry.RecordRetryFailure(ex.Message, now);

                logger.LogWarning(ex,
                    "SupportAttachmentPendingDeletionRetryJob: attempt {AttemptCount} failed to delete {StorageKey} — will retry on the next sweep.",
                    entry.AttemptCount, UploadedAttachmentCleanupScope.RedactStorageKey(entry.StorageKey));
            }
        }

        await db.SaveChangesAsync();
    }
}
