using HR.Infrastructure.Abstractions;
using HR.Modules.Support.Domain;
using HR.Modules.Support.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Support.Services;

/// <summary>
/// Reliability review issue 4 (P1): guarantees cleanup of every storage key acquired during an
/// attachment upload batch, on every failure path — not just the two the handlers used to check
/// explicitly (a normal scan-failure <c>Result</c> and a thrown exception from
/// <c>SaveChangesAsync</c>). Wrap the whole copy/scan/upload/persist sequence in
/// <c>await using var scope = new UploadedAttachmentCleanupScope(...)</c>, call
/// <see cref="Track"/> immediately after each successful <c>UploadAsync</c>, and call
/// <see cref="Commit"/> only once persistence has actually succeeded. Any other exit from the
/// enclosing method — an early <c>return Result.Failure(...)</c>, a thrown exception from a second
/// file's upload, a stream-copy exception, or caller cancellation — reaches
/// <see cref="DisposeAsync"/> without <see cref="Commit"/> having been called, so every tracked key
/// is cleaned up automatically regardless of which specific step failed.
/// </summary>
internal sealed class UploadedAttachmentCleanupScope(
    ISupportAttachmentStorageService storage,
    SupportDbContext db,
    IClock clock,
    IExecutionContextAccessor executionContextAccessor,
    ILogger logger) : IAsyncDisposable
{
    private readonly List<string> _keys = [];
    private bool _committed;

    public void Track(string storageKey) => _keys.Add(storageKey);

    /// <summary>Call once the batch is durably persisted — cancels cleanup for every tracked key.</summary>
    public void Commit() => _committed = true;

    public async ValueTask DisposeAsync()
    {
        if (_committed || _keys.Count == 0)
            return;

        var correlationId = executionContextAccessor.Current?.CorrelationId;

        foreach (var key in _keys)
        {
            // Reliability review issue 4 (P1): cleanup must never reuse the caller's (possibly
            // already-cancelled) token — it needs its own short, independently bounded deadline so
            // a cancelled request still gets a real best-effort delete attempt.
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            try
            {
                await storage.DeleteAsync(key, cleanupCts.Token);
            }
            catch (Exception ex)
            {
                // Reliability review issue 4 (P1): delete failures must never be silently
                // swallowed — log with correlation context and record a durable retry entry so
                // SupportAttachmentPendingDeletionRetryJob can complete the cleanup later.
                logger.LogWarning(ex,
                    "Failed to clean up orphaned support attachment {StorageKey} (correlation {CorrelationId}) — recording for durable retry.",
                    key, correlationId);

                try
                {
                    var now = clock.UtcNowOffset();
                    db.SupportAttachmentPendingDeletions.Add(
                        SupportAttachmentPendingDeletion.Create(Guid.NewGuid(), key, ex.Message, now));
                    await db.SaveChangesAsync(CancellationToken.None);
                }
                catch (Exception recordEx)
                {
                    logger.LogError(recordEx,
                        "Failed to record a pending deletion for orphaned support attachment {StorageKey} (correlation {CorrelationId}) — this blob may leak until manually cleaned up.",
                        key, correlationId);
                }
            }
        }
    }
}
