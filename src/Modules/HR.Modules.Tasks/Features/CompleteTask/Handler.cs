using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.Modules.Tasks.Services;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using HR.Infrastructure.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Tasks.Features.CompleteTask;

internal sealed class CompleteTaskHandler(
    TasksDbContext dbContext,
    INotificationWriter notificationWriter,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    TaskCompletionDispatcher dispatcher,
    TasksResourceAuthorizer resourceAuthorizer)
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

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, CompleteTaskResponse>(
                scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
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

        // SEC-003 / IAM-07: only the assignee, the assignee's manager (anywhere in the
        // reporting hierarchy), or an HR Administrator may complete a task. Endpoint-level
        // Policies("role:employee") only proves tenant membership, not resource ownership —
        // the task's specific assignee is only known after this DB lookup, so the check must
        // live here rather than at the endpoint. This runs before the cancelled-status check
        // and before task.Complete()'s already-completed short-circuit, so an unauthorized
        // caller can never use either as a bypass.
        //
        // Employee ID and User ID are the same value by construction throughout this app (see
        // GetMyTasksHandler.cs), so AssignedUserId is a valid fallback when AssignedEmployeeId
        // is null — a task assigned only via AssignedUserId must still be reachable by that
        // person's manager. Unassigned tasks (both null) have no self/hierarchy path — only the
        // HR-administrator override (evaluated inside CanAccessEmployeeTasksAsync) can complete
        // them, since targetEmployeeId would equal request.CompletedBy only coincidentally.
        var effectiveAssigneeId = task.AssignedEmployeeId ?? task.AssignedUserId;

        var isAuthorized = effectiveAssigneeId.HasValue
            ? await resourceAuthorizer.CanAccessEmployeeTasksAsync(
                task.CompanyId, request.CompletedBy, effectiveAssigneeId.Value, cancellationToken)
            : await resourceAuthorizer.IsHrAdministratorAsync(request.CompletedBy, cancellationToken);

        if (!isAuthorized)
            return Result.Failure<CompleteTaskResponse>(
                Error.Forbidden("You are not authorized to complete this task."));

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

        // Ticket 3 (P1): validate/execute the underlying business action BEFORE the task is marked
        // Completed and saved. A previous version completed the task first, then discovered
        // rejected/invalid outcomes too late to do anything but silently leave the task Completed
        // with no matching business-state change (e.g. a rejected leave approval, or a malformed
        // probation extension). Dispatching first means a failure here leaves the task untouched —
        // still Open/InProgress and actionable — and the caller gets a real error back.
        if (!wasAlreadyCompleted)
        {
            var dispatchResult = await dispatcher.DispatchAsync(new TaskCompletionContext(
                task.CompanyId,
                task.Id,
                task.Title,
                task.Description,
                task.Source,
                task.ActionType,
                task.AssignedEmployeeId,
                request.CompletedBy,
                now,
                task.SourceEntityId,
                request.OutcomeDecision,
                request.OutcomeReason), cancellationToken);

            if (!dispatchResult.IsSuccess)
                return Result.Failure<CompleteTaskResponse>(dispatchResult.Error);
        }

        task.Complete(request.CompletedBy, now);

        // Built from in-memory values ahead of the save, so it can double as both the response and
        // the payload persisted for an idempotency replay.
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
            task.UpdatedAt);

        if (request.IdempotencyKey is { } key)
        {
            var outcome = await dbContext.SaveIdempotentAsync(dbContext.IdempotencyRecords,
                scope, key, fingerprint!, StatusCodes.Status200OK, response, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
            {
                // Lost a race against a concurrent duplicate under the same key - this attempt's
                // completion was rolled back along with it, so skip our own
                // notification/audit/dispatch and hand back the winner's result untouched.
                return Result.Success(outcome.Response!);
            }
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (wasAlreadyCompleted)
        {
            return Result.Success(response);
        }

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

        await auditPublisher.PublishAsync(new TaskCompletedAuditEvent(
            task.CompanyId,
            task.Id,
            task.CompletedBy!.Value,
            previousStatus,
            task.AssignedEmployeeId,
            task.CompletedAt!.Value), cancellationToken);

        return Result.Success(response);
    }
}
