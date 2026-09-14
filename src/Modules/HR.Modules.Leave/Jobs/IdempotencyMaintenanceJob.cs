using Hangfire;
using HR.Modules.Leave.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using HR.SharedKernel.Outbox;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Leave.Jobs;

/// <summary>
/// Ticket 3 (P1) follow-up items 4/5/6: dispatches due audit-outbox entries and cleans up expired
/// idempotency records for this module. <see cref="DisableConcurrentExecutionAttribute"/> takes a
/// distributed lock keyed by job id, so an overrun run and the next scheduled tick can never process
/// the same backlog concurrently.
/// </summary>
internal sealed class IdempotencyMaintenanceJob(
    LeaveDbContext dbContext,
    IAuditEventPublisher auditPublisher,
    ILogger<IdempotencyMaintenanceJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ExecuteAsync()
    {
        var now = DateTimeOffset.UtcNow;

        await dbContext.DispatchPendingAsync(
            dbContext.AuditOutboxEntries, auditPublisher, now, IdempotencyCleanupExtensions.DefaultBatchSize,
            logger, CancellationToken.None);

        await dbContext.IdempotencyRecords.CleanupExpiredIdempotencyRecordsWithLoggingAsync(
            now, logger, "Leave", CancellationToken.None);
    }
}
