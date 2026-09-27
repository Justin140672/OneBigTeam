using HR.Infrastructure.Abstractions;
using HR.Modules.Support.Domain;
using HR.Modules.Support.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
///
/// Security review finding #4 (P1): this scope no longer persists its bookkeeping record through
/// the failed request's own <see cref="SupportDbContext"/>. That context can still be tracking an
/// added <c>SupportRequest</c>, attachment rows, response rows, or other changes from the
/// operation that just failed — a bare <c>ChangeTracker.Clear()</c> is not sufficient, because the
/// same context instance could go on to pick up newly-tracked entities mid-request. Instead, a
/// fresh <see cref="SupportDbContext"/> is resolved from an independent DI scope created via
/// <see cref="IServiceScopeFactory"/>, so the pending-deletion save can never resurrect or commit
/// any part of the failed business graph.
/// </summary>
internal sealed class UploadedAttachmentCleanupScope(
    ISupportAttachmentStorageService storage,
    IServiceScopeFactory serviceScopeFactory,
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
                    RedactStorageKey(key), correlationId);

                await RecordPendingDeletionAsync(key, ex.Message, correlationId);
            }
        }
    }

    /// <summary>
    /// Security review finding #4 (P1): persists the pending-deletion bookkeeping record through a
    /// brand-new DI scope/DbContext instance — genuinely isolated from the failed request's
    /// ambient <see cref="SupportDbContext"/> — so this save can never also commit any part of the
    /// business graph whose operation just failed. Also tolerates the unique constraint on
    /// unresolved storage keys: if a pending-deletion row already exists for this key (e.g. a
    /// concurrent retry or duplicate cleanup attempt), that is treated as success rather than an
    /// error.
    /// </summary>
    private async Task RecordPendingDeletionAsync(string key, string failureReason, string? correlationId)
    {
        try
        {
            await using var scope = serviceScopeFactory.CreateAsyncScope();
            var scopedDb = scope.ServiceProvider.GetRequiredService<SupportDbContext>();

            var now = clock.UtcNowOffset();
            scopedDb.SupportAttachmentPendingDeletions.Add(
                SupportAttachmentPendingDeletion.Create(Guid.NewGuid(), key, failureReason, now));

            bool duplicate;
            try
            {
                await scopedDb.SaveChangesAsync(CancellationToken.None);
                duplicate = false;
            }
            catch (DbUpdateException)
            {
                // An unresolved pending-deletion record for this key may already exist (unique
                // index on storage_key where resolved_at is null) — check before treating this as
                // a genuine failure.
                scopedDb.ChangeTracker.Clear();
                duplicate = await scopedDb.SupportAttachmentPendingDeletions
                    .AsNoTracking()
                    .AnyAsync(d => d.StorageKey == key && d.ResolvedAt == null, CancellationToken.None);

                if (!duplicate)
                    throw;
            }

            if (duplicate)
            {
                // The retry job will pick up the existing record, so this duplicate attempt is a
                // no-op, not a failure.
                logger.LogInformation(
                    "An unresolved pending deletion already exists for support attachment {StorageKey} (correlation {CorrelationId}) — skipping duplicate record.",
                    RedactStorageKey(key), correlationId);
            }
        }
        catch (Exception recordEx)
        {
            logger.LogError(recordEx,
                "Failed to record a pending deletion for orphaned support attachment {StorageKey} (correlation {CorrelationId}) — this blob may leak until manually cleaned up.",
                RedactStorageKey(key), correlationId);
        }
    }

    /// <summary>Storage keys are prefixed with "support/{companyId}/{requestId}/..." — never log
    /// the full key. Only the trailing filename/extension segment is retained for diagnostic
    /// value. Keys are always "{server GUID}{allow-listed extension}" (see SupportAttachmentPolicy),
    /// so the retained tail is already safe; as defence in depth (CodeQL #63-#65) any character
    /// outside [A-Za-z0-9._-] is still replaced with '?', so no control character can reach a log.</summary>
    internal static string RedactStorageKey(string storageKey)
    {
        var lastSlash = storageKey.LastIndexOf('/');
        var tail = lastSlash >= 0 ? storageKey[(lastSlash + 1)..] : storageKey;
        var retained = tail.Length <= 12 ? tail : tail[^12..];
        return "***" + string.Create(retained.Length, retained, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
                span[i] = char.IsAsciiLetterOrDigit(source[i]) || source[i] is '.' or '-' or '_' ? source[i] : '?';
        });
    }
}
