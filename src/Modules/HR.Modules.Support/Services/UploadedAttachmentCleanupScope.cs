using HR.Infrastructure.Abstractions;
using HR.Modules.Support.Domain;
using HR.Modules.Support.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Support.Services;

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

    public void Commit() => _committed = true;

    public async ValueTask DisposeAsync()
    {
        if (_committed || _keys.Count == 0)
            return;

        var correlationId = executionContextAccessor.Current?.CorrelationId;

        foreach (var key in _keys)
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            try
            {
                await storage.DeleteAsync(key, cleanupCts.Token);
            }
            catch (Exception ex)
            {
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
                scopedDb.ChangeTracker.Clear();
                duplicate = await scopedDb.SupportAttachmentPendingDeletions
                    .AsNoTracking()
                    .AnyAsync(d => d.StorageKey == key && d.ResolvedAt == null, CancellationToken.None);

                if (!duplicate)
                    throw;
            }

            if (duplicate)
            {
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
