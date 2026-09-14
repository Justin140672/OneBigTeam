using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.SharedKernel.Idempotency;

/// <summary>
/// Ticket 3 (P1) follow-up item 4/6: one shared cleanup routine every module's own recurring job
/// calls, so batch size, the "keep going until the backlog is drained" loop, and logging shape stay
/// consistent across modules rather than each one reinventing it. Each module still registers its
/// own tiny Hangfire job (see e.g. <c>HR.Modules.Leave.Jobs.IdempotencyMaintenanceJob</c>) - this
/// only holds the part that must not drift between modules.
///
/// Overlap prevention is the calling job's responsibility via
/// <c>[Hangfire.DisableConcurrentExecution]</c> on its own <c>ExecuteAsync</c> - a plain extension
/// method has no way to take a distributed lock itself.
/// </summary>
public static class IdempotencyCleanupExtensions
{
    public const int DefaultBatchSize = 200;

    /// <summary>
    /// Deletes expired idempotency records in bounded batches until the backlog for this call is
    /// drained (a batch smaller than <paramref name="batchSize"/> means nothing expired remains
    /// right now - anything still arriving after that is picked up by the job's next scheduled run,
    /// so a large backlog is worked off over multiple runs rather than blocking one run indefinitely).
    /// Logs the removed count and elapsed time on completion or failure - never the stored responses.
    /// </summary>
    public static async Task<int> CleanupExpiredIdempotencyRecordsWithLoggingAsync<TRecord>(
        this DbSet<TRecord> records,
        DateTimeOffset now,
        ILogger logger,
        string moduleName,
        CancellationToken cancellationToken,
        int batchSize = DefaultBatchSize)
        where TRecord : class, IIdempotencyRecord
    {
        var stopwatch = Stopwatch.StartNew();
        var total = 0;

        try
        {
            int removedInBatch;
            do
            {
                removedInBatch = await records.CleanupExpiredIdempotencyRecordsAsync(now, batchSize, cancellationToken);
                total += removedInBatch;
            }
            while (removedInBatch == batchSize);
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "{Module} idempotency cleanup failed after removing {Removed} record(s) in {ElapsedMs}ms.",
                moduleName, total, stopwatch.ElapsedMilliseconds);
            throw;
        }

        if (total > 0)
        {
            logger.LogInformation(
                "{Module} idempotency cleanup removed {Removed} expired record(s) in {ElapsedMs}ms.",
                moduleName, total, stopwatch.ElapsedMilliseconds);
        }

        return total;
    }
}
