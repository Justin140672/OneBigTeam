using Hangfire;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.SharedKernel;
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
/// notification. Audit publishing has no natural dedupe key here, so a retry may in rare cases
/// publish a duplicate TaskCompletedAuditEvent — accepted as a lesser risk than losing the audit
/// trail entirely, consistent with "notification failures cannot permanently block business
/// processing" (the business action and TaskItem completion are never re-run by this job).
/// </summary>
[AutomaticRetry(Attempts = MaxAttempts, DelaysInSeconds = new[] { 15, 60, 300 })]
internal sealed class TaskCompletionEffectsJob(
    TasksDbContext dbContext,
    INotificationWriter notificationWriter,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    ILogger<TaskCompletionEffectsJob> logger)
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

        // Already fully applied (a prior attempt that succeeded but crashed before marking
        // Processed, or a duplicate enqueue) — no-op.
        if (operation.Status == TaskCompletionOperation.StatusProcessed)
            return;

        var task = await dbContext.TaskItems.SingleOrDefaultAsync(t => t.Id == operation.TaskId);
        if (task is null)
        {
            logger.LogWarning(
                "TaskCompletionEffectsJob: TaskItem {TaskId} for completion operation {OperationId} no longer exists — marking processed.",
                operation.TaskId, operationId);
            operation.MarkProcessed(clock.UtcNowOffset());
            await dbContext.SaveChangesAsync();
            return;
        }

        var now = clock.UtcNowOffset();
        operation.RecordAttempt(now);
        await dbContext.SaveChangesAsync();

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

            await auditPublisher.PublishAsync(new TaskCompletedAuditEvent(
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
