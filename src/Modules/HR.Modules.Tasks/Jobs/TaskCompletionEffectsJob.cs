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

        switch (operation.Status)
        {
            case TaskCompletionOperation.StatusProcessed:
            case TaskCompletionOperation.StatusEffectsVerified:
            case TaskCompletionOperation.StatusWaived:
                return;
            case TaskCompletionOperation.StatusEffectsTerminalFailure:
            case TaskCompletionOperation.StatusDataIntegrityFailure:
                logger.LogInformation(
                    "TaskCompletionEffectsJob: completion operation {OperationId} (task {TaskId}, company {CompanyId}) is terminal ({Status}, FailureCategory={FailureCategory}) and awaits operator action — skipping.",
                    operationId, operation.TaskId, companyId, operation.Status, operation.FailureCategory);
                return;
        }

        var task = await dbContext.TaskItems.SingleOrDefaultAsync(t => t.Id == operation.TaskId);
        if (task is null)
        {
            await ResolveMissingTaskAsync(operation);
            return;
        }

        var now = clock.UtcNowOffset();

        if (!operation.HasCompletionSnapshot)
        {
            operation.CaptureCompletionSnapshot(
                task.AssignedEmployeeId, task.Title, task.Description, "InProgress", task.CompletedAt ?? now, now);
        }

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
                operation.PreviousTaskStatus ?? "InProgress",
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

            if (isFinalAttempt)
            {
                operation.MarkEffectsTerminalFailure(
                    TaskCompletionOperation.CategoryEffectsRetryLimit, ex.Message, clock.UtcNowOffset());
                await dbContext.SaveChangesAsync();

                logger.LogError(ex,
                    "TaskCompletionEffectsJob: permanently failed to confirm completion side effects and requires operator retry; it will not be re-enqueued automatically. The task remains Completed; only notification/audit confirmation is outstanding. CompanyId={CompanyId} TaskId={TaskId} TasksOperationId={TasksOperationId} AttemptCount={AttemptCount} ResetCount={ResetCount} FailureCategory={FailureCategory} TerminalFailureAt={TerminalFailureAt} CorrelationId={CorrelationId}",
                    companyId, task.Id, operationId, operation.AttemptCount, operation.ResetCount,
                    operation.FailureCategory, operation.TerminalFailureAt, operation.CorrelationId);
                return;
            }

            operation.RecordFailure(ex.Message);
            await dbContext.SaveChangesAsync();

            logger.LogWarning(ex,
                "TaskCompletionEffectsJob: attempt {AttemptCount} failed for task {TaskId} (operation {OperationId}) — will retry.",
                operation.AttemptCount, task.Id, operationId);

            throw;
        }
    }

    private async Task DeliverFromSnapshotAsync(TaskCompletionOperation operation, DateTimeOffset now)
    {
        operation.RecordAttempt(now);
        await dbContext.SaveChangesAsync();

        var completedAt = operation.TaskCompletedAt ?? now;

        try
        {
            if (operation.NotificationRequired && operation.SnapshotAssignedEmployeeId is { } employeeId
                && !await notificationWriter.ExistsAsync(employeeId, operation.TaskId, NotificationType.TaskCompleted))
            {
                await notificationWriter.WriteAsync(
                    Guid.NewGuid(), operation.CompanyId, employeeId,
                    $"Task completed: {operation.SnapshotTaskTitle}",
                    null,
                    operation.TaskId,
                    NotificationType.TaskCompleted,
                    NotificationPriority.Normal,
                    completedAt);
            }

            await auditDelivery.EnsureDeliveredAsync(new TaskCompletedAuditEvent(
                operation.CompanyId,
                operation.TaskId,
                operation.CompletedBy,
                operation.PreviousTaskStatus ?? "InProgress",
                operation.SnapshotAssignedEmployeeId,
                completedAt), CancellationToken.None);

            operation.MarkProcessed(clock.UtcNowOffset());
            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            if (operation.AttemptCount >= MaxAttempts)
            {
                operation.MarkEffectsTerminalFailure(
                    TaskCompletionOperation.CategoryEffectsRetryLimit, ex.Message, clock.UtcNowOffset());
                await dbContext.SaveChangesAsync();

                logger.LogError(ex,
                    "TaskCompletionEffectsJob: adjudicated retry could not confirm effects from the supplied evidence and requires operator retry. CompanyId={CompanyId} TaskId={TaskId} TasksOperationId={TasksOperationId} FailureCategory={FailureCategory}",
                    operation.CompanyId, operation.TaskId, operation.Id, operation.FailureCategory);
                return;
            }

            operation.RecordFailure(ex.Message);
            await dbContext.SaveChangesAsync();
            throw;
        }
    }

    private async Task ResolveMissingTaskAsync(TaskCompletionOperation operation)
    {
        var now = clock.UtcNowOffset();

        if (!operation.HasCompletionSnapshot)
        {
            logger.LogError(
                "TaskCompletionEffectsJob: TaskItem {TaskId} for completion operation {OperationId} (company {CompanyId}) no longer exists and the operation holds no completion evidence. Terminal=true FailureCategory={FailureCategory}; flagged for investigation, success is never inferred.",
                operation.TaskId, operation.Id, operation.CompanyId, TaskCompletionOperation.CategoryEvidenceMissing);
            operation.MarkDataIntegrityFailure(
                "Data integrity failure: the completed task no longer exists and the operation holds no completion evidence.",
                now, TaskCompletionOperation.CategoryEvidenceMissing);
            await dbContext.SaveChangesAsync();
            return;
        }

        if (operation.ResolutionType == TaskCompletionOperation.ResolutionEvidenceRetry)
        {
            await DeliverFromSnapshotAsync(operation, now);
            return;
        }

        var auditConfirmed = await auditDelivery.ExistsAsync(operation.TaskId, CancellationToken.None);
        var notificationConfirmed = !operation.NotificationRequired
            || (operation.SnapshotAssignedEmployeeId is { } employeeId
                && await notificationWriter.ExistsAsync(employeeId, operation.TaskId, NotificationType.TaskCompleted));

        if (auditConfirmed && notificationConfirmed)
        {
            logger.LogWarning(
                "TaskCompletionEffectsJob: TaskItem {TaskId} for completion operation {OperationId} (company {CompanyId}) no longer exists but every required completion effect is confirmed from persisted evidence — marking processed.",
                operation.TaskId, operation.Id, operation.CompanyId);
            operation.MarkProcessed(now);
        }
        else
        {
            var category = auditConfirmed
                ? TaskCompletionOperation.CategoryNotificationUnconfirmed
                : TaskCompletionOperation.CategoryAuditUnconfirmed;

            logger.LogError(
                "TaskCompletionEffectsJob: TaskItem {TaskId} for completion operation {OperationId} (company {CompanyId}) no longer exists and a required completion effect is unconfirmed. Terminal=true FailureCategory={FailureCategory} AuditConfirmed={AuditConfirmed} NotificationConfirmed={NotificationConfirmed}; flagged for investigation.",
                operation.TaskId, operation.Id, operation.CompanyId, category, auditConfirmed, notificationConfirmed);
            operation.MarkDataIntegrityFailure(
                $"Data integrity failure: the completed task no longer exists and its {(auditConfirmed ? "completion notification" : "completion audit event")} is missing.",
                now, category);
        }

        await dbContext.SaveChangesAsync();
    }
}
