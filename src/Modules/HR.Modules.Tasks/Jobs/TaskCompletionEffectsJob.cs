using Hangfire;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Tasks.Jobs;

/// <summary>
/// Ticket 4 (P1): retries the notification-write and audit-publish side effects of a task
/// completion whose underlying business action already succeeded and whose TaskItem was already
/// marked Completed (see CompleteTaskHandler), but whose side effects raised/could not be confirmed
/// inline (e.g. a transient DB/notification-store failure). Mirrors
/// HR.Modules.Identity.Jobs.AccountDisablementJob's idempotency-by-row-status +
/// [AutomaticRetry] + attempt-tracking shape.
///
/// Idempotent: re-checks INotificationWriter.ExistsAsync before writing, so a retry after a
/// partial failure (e.g. notification written, audit publish then failed) never double-writes the
/// notification. The task-completed audit event has a deterministic EventId (the task id), so
/// delivery goes through TaskCompletionAuditDelivery: publish only when absent and treat the
/// operation as Processed only once the event is confirmed to exist (the global publisher swallows
/// persistence failures, so a normal return proves nothing). An unconfirmed audit event throws and
/// leaves the operation outstanding for the next retry. The business action and TaskItem completion
/// are never re-run by this job.
/// </summary>
[AutomaticRetry(Attempts = MaxAttempts, DelaysInSeconds = new[] { 15, 60, 300 })]
internal sealed class TaskCompletionEffectsJob(
    TasksDbContext dbContext,
    INotificationWriter notificationWriter,
    IClock clock,
    TaskCompletionAuditDelivery auditDelivery,
    ILogger<TaskCompletionEffectsJob> logger,
    IExecutionContextAccessor? executionContextAccessor = null)
{
    public const int MaxAttempts = 4;

    public async Task ProcessAsync(Guid operationId, Guid companyId)
    {
        var operation = await dbContext.TaskCompletionOperations
            .SingleOrDefaultAsync(o => o.Id == operationId);

        if (operation is null)
        {
            logger.LogWarning(
                "TaskCompletionEffectsJob: no completion operation found for id {OperationId} — skipping.",
                operationId);
            return;
        }

        if (operation.CompanyId != companyId)
        {
            throw new InvalidOperationException(
                $"TaskCompletionOperation {operationId} does not belong to company {companyId}.");
        }

        if (operation.Status == TaskCompletionOperation.StatusProcessed)
            return;

        var task = await dbContext.TaskItems.SingleOrDefaultAsync(t => t.Id == operation.TaskId);
        if (task is null)
        {
            if (await auditDelivery.ExistsAsync(operation.TaskId, CancellationToken.None))
            {
                logger.LogWarning(
                    "TaskCompletionEffectsJob: TaskItem {TaskId} for completion operation {OperationId} (company {CompanyId}) no longer exists but its completion audit event is confirmed — marking processed.",
                    operation.TaskId, operationId, companyId);
                operation.MarkProcessed(clock.UtcNowOffset());
            }
            else
            {
                logger.LogError(
                    "TaskCompletionEffectsJob: TaskItem {TaskId} for completion operation {OperationId} (company {CompanyId}) no longer exists and its completion audit event was never confirmed. Terminal=true FailureCategory=task_missing; the operation is flagged for investigation and will not be retried.",
                    operation.TaskId, operationId, companyId);
                operation.MarkDataIntegrityFailure(
                    "Data integrity failure: the completed task no longer exists and its completion audit event is missing.",
                    clock.UtcNowOffset());
            }

            await dbContext.SaveChangesAsync();
            return;
        }

        var now = clock.UtcNowOffset();
        operation.RecordAttempt(now);
        await dbContext.SaveChangesAsync();

        // Ticket 23 (P2): restore the persisted correlation/causation/message ids for the duration
        // of this resumed background work (Origin = ReconciliationJob), so the resulting audit event
        // carries the SAME correlation id as the original CompleteTask request even after a process
        // restart between the original attempt and this retry. Rows written before this migration
        // (all-null metadata) fall back to a fresh root context rather than throwing.
        var restoredContext = operation.CorrelationId is { } correlationId
            ? ExecutionContextInfo.Restore(
                correlationId.ToString("D"), operation.MessageId ?? Guid.NewGuid(), operation.CausationId,
                ExecutionOrigin.ReconciliationJob)
            : ExecutionContextInfo.NewRoot(ExecutionOrigin.ReconciliationJob);

        using var _ = executionContextAccessor?.Push(restoredContext);

        try
        {
            if (task.AssignedEmployeeId.HasValue)
            {
                var alreadySent = await notificationWriter.ExistsAsync(
                    task.AssignedEmployeeId.Value, task.Id, NotificationType.TaskCompleted);

                if (!alreadySent)
                {
                    await notificationWriter.WriteAsync(
                        Guid.NewGuid(), task.CompanyId, task.AssignedEmployeeId.Value,
                        $"Task completed: {task.Title}",
                        null,
                        task.Id,
                        NotificationType.TaskCompleted,
                        NotificationPriority.Normal,
                        now);
                }
            }

            await auditDelivery.EnsureDeliveredAsync(new TaskCompletedAuditEvent(
                task.CompanyId,
                task.Id,
                operation.CompletedBy,
                "InProgress",
                task.AssignedEmployeeId,
                task.CompletedAt ?? now), CancellationToken.None);

            operation.MarkProcessed(clock.UtcNowOffset());
            await dbContext.SaveChangesAsync();

            logger.LogInformation(
                "TaskCompletionEffectsJob: confirmed completion side effects for task {TaskId} (operation {OperationId}, company {CompanyId}).",
                task.Id, operationId, companyId);
        }
        catch (Exception ex)
        {
            var isFinalAttempt = operation.AttemptCount >= MaxAttempts;

            operation.RecordFailure(ex.Message);
            await dbContext.SaveChangesAsync();

            if (isFinalAttempt)
            {
                logger.LogError(ex,
                    "TaskCompletionEffectsJob: permanently failed to confirm completion side effects for task {TaskId} (operation {OperationId}) after {Attempts} attempts. The task itself remains Completed; only notification/audit confirmation is outstanding.",
                    task.Id, operationId, MaxAttempts);
            }
            else
            {
                logger.LogWarning(ex,
                    "TaskCompletionEffectsJob: attempt {AttemptCount} failed for task {TaskId} (operation {OperationId}) — will retry.",
                    operation.AttemptCount, task.Id, operationId);
            }

            throw;
        }
    }
}
