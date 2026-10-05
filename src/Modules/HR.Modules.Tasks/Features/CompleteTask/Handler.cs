using Hangfire;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Jobs;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using HR.SharedKernel.Idempotency;
using HR.Infrastructure.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Tasks.Features.CompleteTask;

internal sealed class CompleteTaskHandler(
    TasksDbContext dbContext,
    INotificationWriter notificationWriter,
    IClock clock,
    TaskCompletionAuditDelivery auditDelivery,
    TaskCompletionDispatcher dispatcher,
    TasksResourceAuthorizer resourceAuthorizer,
    IBackgroundJobClient backgroundJobClient,
    ILogger<CompleteTaskHandler> logger,
    // Ticket 23 (P2): reference wiring for the Tasks module - optional so existing direct
    // constructions in unit tests are unaffected; production DI always supplies the real singleton.
    IExecutionContextAccessor? executionContextAccessor = null)
{
    public async Task<Result<CompleteTaskResponse>> HandleAsync(
        CompleteTaskRequest request,
        CancellationToken cancellationToken)
    {
        // Ticket 3 (P1) follow-up: dedupe a retried/duplicated request before doing any business
        // work, so a repeated delivery can't double-fire completion side effects (notification,
        // audit, downstream dispatch). Checked ahead of the atomic insert-or-replay in
        // SaveIdempotentAsync below, which also catches a same-key request that races in
        // concurrently. This is a distinct concern from the existing wasAlreadyCompleted
        // domain-state check below, which guards against a second completion of an
        // already-Completed task regardless of key.
        var scope = new IdempotencyScope(GetType().Name, request.CompanyId, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        CompleteTaskResponse? storedResponse = null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, CompleteTaskResponse>(
                scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    storedResponse = replay.Response!;
                    break;
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<CompleteTaskResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        var task = await dbContext.TaskItems
            .SingleOrDefaultAsync(
                t => t.Id == request.Id && t.CompanyId == request.CompanyId,
                cancellationToken);

        if (task is null)
            return Result.Failure<CompleteTaskResponse>(
                Error.NotFound($"Task with id '{request.Id}' was not found."));

        var effectiveAssigneeId = task.AssignedEmployeeId ?? task.AssignedUserId;

        var isAuthorized = effectiveAssigneeId.HasValue
            ? await resourceAuthorizer.CanAccessEmployeeTasksAsync(
                task.CompanyId, request.CompletedBy, effectiveAssigneeId.Value, cancellationToken)
            : await resourceAuthorizer.IsHrAdministratorAsync(request.CompletedBy, cancellationToken);

        if (!isAuthorized)
            return Result.Failure<CompleteTaskResponse>(
                Error.Forbidden("You are not authorized to complete this task."));

        if (storedResponse is not null)
            return await LiveReplayAsync(task.CompanyId, task.Id, storedResponse, cancellationToken);

        if (task.Status == TaskItemStatus.Cancelled)
            return Result.Failure<CompleteTaskResponse>(
                Error.Conflict("Cannot complete a cancelled task."));

        var previousStatus = task.Status.ToString();

        // Idempotency: TaskItem.Complete() is itself a no-op when the task is already
        // Completed (see TaskItem.cs), but the handler was previously writing a fresh
        // notification/audit event/dispatch on every call regardless — a second completion
        // request for an already-completed task would violate the notifications table's
        // (employee_id, source_entity_id, type) uniqueness constraint and 500, plus fire
        // duplicate audit events and downstream actions (e.g. leave/probation/asset
        // completion side effects) a second time. Side effects must only fire on the actual
        // Open/InProgress -> Completed transition, not on a repeat call.
        var wasAlreadyCompleted = task.Status == TaskItemStatus.Completed;

        var now = clock.UtcNowOffset();
        TaskCompletionOperation? existingOperation = null;

        // Ticket 3 (P1): validate/execute the underlying business action BEFORE the task is marked
        // Completed and saved. A previous version completed the task first, then discovered
        // rejected/invalid outcomes too late to do anything but silently leave the task Completed
        // with no matching business-state change (e.g. a rejected leave approval, or a malformed
        // probation extension). Dispatching first means a failure here leaves the task untouched —
        // still Open/InProgress and actionable — and the caller gets a real error back.
        // Ticket 4 (P1): persist the completion request — including the decision payload — as a
        // durable TaskCompletionOperation BEFORE the business dispatch runs, so the caller's
        // decision/reason survive a crash between "request received" and "business action applied"
        // even though the dispatch itself is still synchronous. The operation's Status then tracks
        // exactly how far completion got: Rejected (dispatch declined it, task never completed),
        // DispatchApplied (business action + TaskItem completion both committed, side effects still
        // pending/retrying), or Processed (fully applied).
        TaskCompletionOperation? operation = null;

        if (wasAlreadyCompleted)
        {
            var existing = await dbContext.TaskCompletionOperations.AsNoTracking()
                .Where(o => o.TaskId == task.Id && o.Status != TaskCompletionOperation.StatusRejected)
                .OrderByDescending(o => o.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (existing is null)
            {
                logger.LogInformation(
                    "CompleteTaskHandler: task {TaskId} (company {CompanyId}) is already completed and has no completion operation (legacy task); no effects are replayed.",
                    task.Id, task.CompanyId);
            }
            else
            {
                existingOperation = existing;

                if (existing.IsTerminalFailure)
                {
                    logger.LogError(
                        "CompleteTaskHandler: completion retry for task {TaskId} (company {CompanyId}) hit terminal operation {OperationId} ({Status}, FailureCategory={FailureCategory}); operator action required.",
                        task.Id, task.CompanyId, existing.Id, existing.Status, existing.FailureCategory);
                    return Result.Failure<CompleteTaskResponse>(CompletionStatusMapper.TerminalConflict(existing));
                }
            }
        }

        if (!wasAlreadyCompleted)
        {
            // Ticket 11 (P1): reuse an existing non-Rejected operation for this task rather than
            // blindly creating a new one — recovers using the ORIGINAL actor/decision/reason if a
            // prior attempt already got this far (crash before dispatch, or before this save), and
            // converges a retried/racing request onto the same operation instead of an unrelated
            // second one. A task whose only prior operation was Rejected is free to get a new one,
            // since no business mutation applied for that attempt.
            operation = await dbContext.TaskCompletionOperations
                .Where(o => o.TaskId == task.Id && o.Status != TaskCompletionOperation.StatusRejected)
                .OrderByDescending(o => o.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (operation is null)
            {
                operation = TaskCompletionOperation.CreatePending(
                    Guid.NewGuid(), task.CompanyId, task.Id, request.CompletedBy,
                    request.OutcomeDecision, request.OutcomeReason, now,
                    executionContextAccessor?.Current);
                dbContext.TaskCompletionOperations.Add(operation);

                try
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException ex) when (PostgresUniqueViolation.Is(
                    ex, "ix_task_completion_operations_task_id_active"))
                {
                    var entry = dbContext.Entry(operation);
                    if (entry.State != EntityState.Detached)
                        entry.State = EntityState.Detached;

                    operation = await dbContext.TaskCompletionOperations
                        .Where(o => o.TaskId == task.Id && o.Status != TaskCompletionOperation.StatusRejected)
                        .OrderByDescending(o => o.CreatedAt)
                        .FirstAsync(cancellationToken);
                }
            }

            if (operation.IsTerminalFailure)
            {
                logger.LogError(
                    "CompleteTaskHandler: completion operation {OperationId} for task {TaskId} (company {CompanyId}) is in terminal state {Status} (FailureCategory={FailureCategory}) and requires operator action.",
                    operation.Id, task.Id, task.CompanyId, operation.Status, operation.FailureCategory);
                return Result.Failure<CompleteTaskResponse>(CompletionStatusMapper.TerminalConflict(operation));
            }

            if (operation.Status == TaskCompletionOperation.StatusPending)
            {
                var dispatchResult = await dispatcher.DispatchAsync(new TaskCompletionContext(
                    task.CompanyId,
                    task.Id,
                    task.Title,
                    task.Description,
                    task.Source,
                    task.ActionType,
                    task.AssignedEmployeeId,
                    operation.CompletedBy,
                    now,
                    task.SourceEntityId,
                    operation.OutcomeDecision,
                    operation.OutcomeReason,
                    operation.Id), cancellationToken);

                if (!dispatchResult.IsSuccess)
                {
                    operation.MarkRejected(dispatchResult.Error.Message, now);
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return Result.Failure<CompleteTaskResponse>(dispatchResult.Error);
                }

                operation.MarkDispatchApplied(now);
            }
        }

        // Ticket 11 (P1): when a persisted operation was reused (resumed/retried), the task must be
        // completed as its ORIGINAL actor — not whoever issued this particular retry — matching the
        // decision/reason that was actually dispatched.
        task.Complete(operation?.CompletedBy ?? request.CompletedBy, now);

        operation?.CaptureCompletionSnapshot(
            task.AssignedEmployeeId, task.Title, task.Description, previousStatus, task.CompletedAt ?? now, now);

        var response = new CompleteTaskResponse(
            task.Id,
            task.CompanyId,
            task.Title,
            task.Description,
            task.Status.ToString(),
            task.Priority.ToString(),
            task.Source.ToString(),
            task.DueDate,
            task.AssignedEmployeeId,
            task.AssignedUserId,
            task.CreatedBy,
            task.CompletedBy,
            task.CompletedAt,
            task.CreatedAt,
            task.UpdatedAt,
            CompletionStatusMapper.EffectsPending);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await dbContext.SaveIdempotentAsync(dbContext.IdempotencyRecords,
                scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
            {
                return await LiveReplayAsync(task.CompanyId, task.Id, outcome.Response!, cancellationToken);
            }
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (wasAlreadyCompleted)
        {
            return existingOperation is null
                ? Result.Success(response with { EffectsStatus = null })
                : CompletionStatusMapper.Map(response, existingOperation);
        }

        // Ticket 4 (P1): the business action and the TaskItem's Completed transition are already
        // durably committed above — a failure from here on must never re-run either of them (that
        // would risk duplicating the business action), and must never fail the request back to the
        // caller (the task genuinely is completed; only confirming the notification/audit side
        // effects is outstanding). A failure here hands off to TaskCompletionEffectsJob, which
        // retries just those two idempotent steps against the persisted operation.
        try
        {
            if (task.AssignedEmployeeId.HasValue)
            {
                await notificationWriter.WriteAsync(
                    Guid.NewGuid(), task.CompanyId, task.AssignedEmployeeId.Value,
                    $"Task completed: {task.Title}",
                    null,
                    task.Id,
                    NotificationType.TaskCompleted,
                    NotificationPriority.Normal,
                    now,
                    cancellationToken);
            }

            await auditDelivery.EnsureDeliveredAsync(new TaskCompletedAuditEvent(
                task.CompanyId,
                task.Id,
                task.CompletedBy!.Value,
                previousStatus,
                task.AssignedEmployeeId,
                task.CompletedAt!.Value), cancellationToken);

            operation!.MarkProcessed(clock.UtcNowOffset());
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "CompleteTaskHandler: task {TaskId} was completed but confirming its notification/audit side effects failed inline — handing off to TaskCompletionEffectsJob (operation {OperationId}).",
                task.Id, operation!.Id);

            backgroundJobClient.Enqueue<TaskCompletionEffectsJob>(
                job => job.ProcessAsync(operation.Id, task.CompanyId));
        }

        try
        {
            return await LiveReplayAsync(task.CompanyId, task.Id, response, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "CompleteTaskHandler: could not re-read completion operation {OperationId}; reporting effects as pending.",
                operation!.Id);
            return Result.Success(response);
        }
    }

    private async Task<Result<CompleteTaskResponse>> LiveReplayAsync(
        Guid companyId, Guid taskId, CompleteTaskResponse stored, CancellationToken cancellationToken)
    {
        var current = await dbContext.TaskCompletionOperations.AsNoTracking()
            .Where(o => o.CompanyId == companyId && o.TaskId == taskId && o.Status != TaskCompletionOperation.StatusRejected)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return current is null
            ? Result.Success(stored with { EffectsStatus = null, ResolutionType = null })
            : CompletionStatusMapper.Map(stored, current);
    }
}
