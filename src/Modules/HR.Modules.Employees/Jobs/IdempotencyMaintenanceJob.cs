using Hangfire;
using HR.Modules.Employees.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using HR.SharedKernel.Idempotency;
using HR.SharedKernel.Outbox;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Employees.Jobs;

/// <summary>
/// Ticket 3 (P1) follow-up items 4/5/6: dispatches due audit/integration-outbox entries (e.g.
/// EmployeeCreatedIntegrationEvent, so downstream onboarding/probation/leave/task/notification work
/// survives a crash between commit and delivery) and cleans up expired idempotency records for this
/// module. <see cref="DisableConcurrentExecutionAttribute"/> takes a distributed lock keyed by job
/// id, so an overrun run and the next scheduled tick can never process the same backlog concurrently.
/// </summary>
internal sealed class IdempotencyMaintenanceJob(
    EmployeesDbContext dbContext,
    IAuditEventPublisher auditPublisher,
    IIntegrationEventPublisher integrationPublisher,
    ILogger<IdempotencyMaintenanceJob> logger,
    IExecutionContextAccessor executionContextAccessor)
{
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ExecuteAsync()
    {
        var now = DateTimeOffset.UtcNow;

        // Ticket 23 (P2): this is the durable-redelivery / reconciliation path — DispatchPendingAsync
        // restores each entry's persisted correlation/causation/message ids as the ambient execution
        // context before invoking the publisher (Origin = ReconciliationJob), so a delayed retry after
        // a process restart carries the same identity as when it was originally staged.
        await dbContext.DispatchPendingAsync(
            dbContext.AuditOutboxEntries, auditPublisher, now, IdempotencyCleanupExtensions.DefaultBatchSize,
            logger, CancellationToken.None, integrationPublisher, executionContextAccessor);

        await dbContext.IdempotencyRecords.CleanupExpiredIdempotencyRecordsWithLoggingAsync(
            now, logger, "Employees", CancellationToken.None);
    }
}
