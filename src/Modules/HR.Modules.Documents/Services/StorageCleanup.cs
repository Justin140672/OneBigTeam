using HR.Infrastructure.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Documents.Services;

/// <summary>
/// Best-effort compensating storage deletion used when a database write fails (or a replaced object is
/// no longer referenced) after a file has already been uploaded. A failed cleanup must never replace or
/// mask the original failure, so this helper never throws; instead the failure is logged separately
/// with safe identifiers (operation, storage key, company/entity ids only — never file contents, names
/// or tokens) so the orphaned object can be located and removed.
/// Cleanup deliberately uses <see cref="CancellationToken.None"/>: the usual reason the surrounding
/// operation failed is caller cancellation, and a cancelled token would make the cleanup fail too.
/// </summary>
internal static class StorageCleanup
{
    public static Task TryDeleteAsync(
        IDocumentStorageService storage,
        ILogger? logger,
        string operation,
        string storageKey,
        Guid companyId,
        Guid? entityId)
        => TryDeleteCoreAsync(storage.DeleteAsync, logger, operation, storageKey, companyId, entityId);

    public static Task TryDeleteAsync(
        IProfilePhotoStorageService storage,
        ILogger? logger,
        string operation,
        string storageKey,
        Guid companyId,
        Guid? entityId)
        => TryDeleteCoreAsync(storage.DeleteAsync, logger, operation, storageKey, companyId, entityId);

    private static async Task TryDeleteCoreAsync(
        Func<string, CancellationToken, Task> delete,
        ILogger? logger,
        string operation,
        string storageKey,
        Guid companyId,
        Guid? entityId)
    {
        try
        {
            await delete(storageKey, CancellationToken.None);
        }
        catch (Exception cleanupException)
        {
            (logger ?? NullLogger.Instance).LogError(
                cleanupException,
                "Storage cleanup failed; the stored object may be orphaned and needs manual removal. " +
                "Operation={Operation} StorageKey={StorageKey} CompanyId={CompanyId} EntityId={EntityId}",
                operation, storageKey, companyId, entityId);
        }
    }
}
