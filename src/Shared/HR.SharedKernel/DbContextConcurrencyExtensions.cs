using Microsoft.EntityFrameworkCore;

namespace HR.SharedKernel;

/// <summary>
/// Ticket 2 (optimistic concurrency) base code. Single implementation of the "pin the original
/// version, increment, save, translate the stale-save failure" pattern so per-handler code stays
/// minimal and consistent across every module.
/// </summary>
public static class DbContextConcurrencyExtensions
{
    /// <summary>
    /// Saves changes with an optimistic-concurrency guard for <paramref name="aggregate"/>.
    ///
    /// When <paramref name="expectedVersion"/> is supplied, the EF <c>OriginalValue</c> of the
    /// aggregate's <see cref="IVersionedAggregate.Version"/> is pinned to it, so a concurrent
    /// modification since the client loaded the record causes the UPDATE to affect zero rows and
    /// raise <see cref="DbUpdateConcurrencyException"/>. That is caught and returned as
    /// <see cref="Error.Concurrency(string)"/>; nothing is committed, so callers must not publish
    /// audit or integration events for a failed save.
    ///
    /// When <paramref name="expectedVersion"/> is <c>null</c> the save is rejected with
    /// <see cref="Error.Concurrency(string)"/>: an omitted version must never be allowed to silently
    /// overwrite a concurrent edit. Legitimate un-versioned callers (background jobs, internal
    /// server-side flows) must call <see cref="ForceSaveChangesAdvancingVersionAsync{T}"/> instead so
    /// that the intent to write without a client version is explicit.
    /// </summary>
    public static async Task<Result> SaveChangesWithConcurrencyAsync<T>(
        this DbContext dbContext,
        T aggregate,
        int? expectedVersion,
        string conflictMessage,
        CancellationToken cancellationToken)
        where T : class, IVersionedAggregate
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(aggregate);

        if (expectedVersion is not { } expected)
        {
            return Result.Failure(Error.Concurrency(
                "This record was opened without a version and cannot be safely saved. Reload and try again."));
        }

        dbContext.Entry(aggregate).Property(nameof(IVersionedAggregate.Version)).OriginalValue = expected;
        aggregate.IncrementVersion();

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(Error.Concurrency(conflictMessage));
        }
    }

    /// <summary>
    /// Saves changes for <paramref name="aggregate"/> without an optimistic-concurrency guard,
    /// advancing <see cref="IVersionedAggregate.Version"/> so downstream stale saves are detected.
    /// For legitimate callers that intentionally write without a client-supplied version:
    /// background jobs and internal server-side flows. Do not use for client-driven update endpoints.
    /// </summary>
    public static async Task<Result> ForceSaveChangesAdvancingVersionAsync<T>(
        this DbContext dbContext,
        T aggregate,
        CancellationToken cancellationToken)
        where T : class, IVersionedAggregate
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(aggregate);

        aggregate.IncrementVersion();
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
