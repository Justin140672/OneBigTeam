using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Persistence;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Tasks.Services;

internal sealed class TaskCompleter(
    TasksDbContext dbContext,
    INotificationWriter notificationWriter,
    IClock clock,
    TaskCompletionAuditDelivery auditDelivery,
    TaskCompletionDispatcher dispatcher,
    ILogger<TaskCompleter>? logger = null) : ITaskCompleter
{
    public const int MaxRetryableAttempts = 10;

    private const string ProgrammaticStatePrimaryKey = "PK_programmatic_task_completions";

    public async Task CompleteBySourceEntityAsync(
        Guid companyId,
        Guid sourceEntityId,
        TaskSource source,
        TaskActionType actionType,
        Guid completedBy,
        CancellationToken cancellationToken) =>
        await CompleteLoggingFailureAsync(companyId, sourceEntityId, source, actionType, null, completedBy, cancellationToken);

    public async Task CompleteBySourceEntityForEmployeeAsync(
        Guid companyId,
        Guid sourceEntityId,
        TaskSource source,
        TaskActionType actionType,
        Guid assignedEmployeeId,
        Guid completedBy,
        CancellationToken cancellationToken) =>
        await CompleteLoggingFailureAsync(companyId, sourceEntityId, source, actionType, assignedEmployeeId, completedBy, cancellationToken);

    // A business-dispatch failure is durably recorded on the programmatic completion (and logged at
    // error) and is retried or surfaced by ProgrammaticTaskCompletionReconciliationJob; these
    // fire-and-forget callers cannot act on it themselves.
    private async Task CompleteLoggingFailureAsync(
        Guid companyId,
        Guid sourceEntityId,
        TaskSource source,
        TaskActionType actionType,
        Guid? assignedEmployeeId,
        Guid completedBy,
        CancellationToken cancellationToken)
    {
        try
        {
            await CompleteConfirmedAsync(companyId, sourceEntityId, source, actionType, assignedEmployeeId, completedBy, cancellationToken);
        }
        catch (TaskCompletionDispatchException ex)
        {
            logger?.LogError(ex,
                "Programmatic completion of {Source}/{ActionType} task for source entity {SourceEntityId} (company {CompanyId}) is outstanding: {Reason}",
                source, actionType, sourceEntityId, companyId, ex.Message);
        }
    }

    /// <summary>
    /// Completes the open task and drives every completion effect to confirmation. Returns false when
    /// effects are still outstanding (owned by a normal completion operation, or leased by another
    /// worker); throws when an effect fails.
    /// </summary>
    /// <param name="businessEffectAlreadyApplied">
    /// True when the originating operation already applied the business effect the completion action
    /// would otherwise apply (e.g. an interview outcome recorded directly). Dispatch is then recorded
    /// as already applied instead of being re-run; every other effect still runs.
    /// </param>
    public async Task<bool> CompleteConfirmedAsync(
        Guid companyId,
        Guid sourceEntityId,
        TaskSource source,
        TaskActionType actionType,
        Guid? assignedEmployeeId,
        Guid completedBy,
        CancellationToken cancellationToken,
        bool businessEffectAlreadyApplied = false) =>
        ToLegacyResult(await ResolveAsync(
            companyId, sourceEntityId, source, actionType, assignedEmployeeId, completedBy, cancellationToken,
            businessEffectAlreadyApplied));

    /// <summary>
    /// Same as <see cref="CompleteConfirmedAsync"/> but reports a persisted permanent failure as an
    /// explicit <see cref="TaskResolutionStatus.TerminalFailure"/> result instead of throwing.
    /// Non-terminal failures and cancellation still throw.
    /// </summary>
    public async Task<TaskResolutionResult> ResolveAsync(
        Guid companyId,
        Guid sourceEntityId,
        TaskSource source,
        TaskActionType actionType,
        Guid? assignedEmployeeId,
        Guid completedBy,
        CancellationToken cancellationToken,
        bool businessEffectAlreadyApplied = false)
    {
        var task = await dbContext.TaskItems
            .Where(t => t.CompanyId == companyId
                     && t.SourceEntityId == sourceEntityId
                     && t.Source == source
                     && t.ActionType == actionType
                     && (assignedEmployeeId == null || t.AssignedEmployeeId == assignedEmployeeId)
                     && t.Status != TaskItemStatus.Cancelled)
            .OrderBy(t => t.Status == TaskItemStatus.Completed)
            .FirstOrDefaultAsync(cancellationToken);

        if (task is null)
            return TaskResolutionResult.Confirmed();

        var state = await dbContext.ProgrammaticTaskCompletions
            .SingleOrDefaultAsync(c => c.TaskId == task.Id, cancellationToken);

        if (state is not null)
            return await ReconcileResolvedAsync(task, state, cancellationToken);

        // Ownership: TaskCompletionOperation rows (CompleteTaskHandler, TaskCompletionReconciliationJob,
        // TaskCompletionEffectsJob) own every completion that came through a user request. A
        // programmatic completion never competes with an active one.
        var activeOperation = await dbContext.TaskCompletionOperations
            .Where(o => o.TaskId == task.Id && o.Status != TaskCompletionOperation.StatusRejected)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        switch (activeOperation?.Status)
        {
            case TaskCompletionOperation.StatusPending:
                return TaskResolutionResult.Outstanding(task.Id);
            case TaskCompletionOperation.StatusDispatchApplied:
                await RemoveOpenTaskNotificationsAsync(task, cancellationToken);
                return TaskResolutionResult.Outstanding(task.Id);
            case TaskCompletionOperation.StatusProcessed:
                if (task.Status == TaskItemStatus.Completed)
                    await RemoveOpenTaskNotificationsAsync(task, cancellationToken);
                return TaskResolutionResult.Confirmed(task.Id);
        }

        if (task.Status == TaskItemStatus.Completed)
        {
            await RemoveOpenTaskNotificationsAsync(task, cancellationToken);
            return TaskResolutionResult.Confirmed(task.Id);
        }

        var now = clock.UtcNowOffset();
        state = ProgrammaticTaskCompletion.Create(
            task.Id, task.CompanyId, completedBy, task.Status.ToString(), now, businessEffectAlreadyApplied);
        state.Claim(Guid.NewGuid(), now);
        task.Complete(completedBy, now);
        dbContext.ProgrammaticTaskCompletions.Add(state);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException
            || (ex is DbUpdateException dbEx && PostgresUniqueViolation.Is(dbEx, ProgrammaticStatePrimaryKey)))
        {
            dbContext.Entry(state).State = EntityState.Detached;
            await dbContext.Entry(task).ReloadAsync(cancellationToken);

            var winner = await dbContext.ProgrammaticTaskCompletions
                .SingleOrDefaultAsync(c => c.TaskId == task.Id, cancellationToken);

            if (winner is null)
                return TaskResolutionResult.Outstanding(task.Id);

            return await ReconcileResolvedAsync(task, winner, cancellationToken);
        }

        return await ProcessClaimedResolvedAsync(task, state, cancellationToken);
    }

    /// <summary>
    /// Claims the completion (atomically, before any external effect) and drives it to confirmation.
    /// Returns false when another worker holds a live lease or won the claim race; throws for a
    /// terminally failed completion.
    /// </summary>
    public async Task<bool> ReconcileAsync(
        TaskItem task, ProgrammaticTaskCompletion state, CancellationToken cancellationToken) =>
        ToLegacyResult(await ReconcileResolvedAsync(task, state, cancellationToken));

    public async Task<TaskResolutionResult> ReconcileResolvedAsync(
        TaskItem task, ProgrammaticTaskCompletion state, CancellationToken cancellationToken)
    {
        if (state.ConfirmedAt is not null)
            return TaskResolutionResult.Confirmed(task.Id, state.OperationId);

        if (state.IsTerminallyFailed)
            return TerminalResult(state);

        var now = clock.UtcNowOffset();

        if (state.IsLeased(now))
            return TaskResolutionResult.Outstanding(task.Id, state.OperationId);

        var expectedVersion = state.Version;
        state.Claim(Guid.NewGuid(), now);

        var claim = await dbContext.SaveChangesWithConcurrencyAsync(
            state, expectedVersion, "This programmatic task completion is being processed by another worker.", cancellationToken);

        if (claim.IsFailure)
        {
            dbContext.Entry(state).State = EntityState.Detached;
            return TaskResolutionResult.Outstanding(task.Id, state.OperationId);
        }

        return await ProcessClaimedResolvedAsync(task, state, cancellationToken);
    }

    private static TaskResolutionResult TerminalResult(ProgrammaticTaskCompletion state) =>
        TaskResolutionResult.Terminal(state.TaskId, state.OperationId, state.FailureReason, state.TerminalFailureAt);

    private static bool ToLegacyResult(TaskResolutionResult result) => result.Status switch
    {
        TaskResolutionStatus.Confirmed => true,
        TaskResolutionStatus.Outstanding => false,
        _ => throw new TaskCompletionDispatchException(
            $"Programmatic completion of task {result.TaskId} failed permanently: {result.FailureReason}", isTerminal: true),
    };

    private async Task<TaskResolutionResult> ProcessClaimedResolvedAsync(
        TaskItem task, ProgrammaticTaskCompletion state, CancellationToken cancellationToken)
    {
        try
        {
            return await ProcessClaimedAsync(task, state, cancellationToken)
                ? TaskResolutionResult.Confirmed(task.Id, state.OperationId)
                : TaskResolutionResult.Outstanding(task.Id, state.OperationId);
        }
        catch (TaskCompletionDispatchException ex)
            when (ex.IsTerminal && state.IsTerminallyFailed && dbContext.Entry(state).State != EntityState.Detached)
        {
            return TerminalResult(state);
        }
    }

    private async Task<bool> ProcessClaimedAsync(
        TaskItem task, ProgrammaticTaskCompletion state, CancellationToken cancellationToken)
    {
        try
        {
            await RunEffectsAsync(task, state, cancellationToken);

            state.Confirm(clock.UtcNowOffset());
            await SaveCheckpointAsync(state, cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.Entry(state).State = EntityState.Detached;
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var terminal = ex is TaskCompletionDispatchException { IsTerminal: true }
                || state.AttemptCount >= MaxRetryableAttempts;

            if (terminal)
            {
                logger?.LogError(ex,
                    "Programmatic completion of task {TaskId} (company {CompanyId}, operation {OperationId}) failed permanently after {Attempts} attempt(s) and requires operator reset; it will not be retried automatically. Terminal=true FailureCategory={FailureCategory}: {Reason}",
                    state.TaskId, state.CompanyId, state.OperationId, state.AttemptCount, ex.GetType().Name, ex.Message);
            }
            else
            {
                logger?.LogWarning(ex,
                    "Programmatic completion of task {TaskId} (company {CompanyId}, operation {OperationId}) attempt {Attempts} failed and will be retried.",
                    state.TaskId, state.CompanyId, state.OperationId, state.AttemptCount);
            }

            try
            {
                state.RecordFailure(ex.Message, terminal, clock.UtcNowOffset());
                await SaveCheckpointAsync(state, cancellationToken);
            }
            catch (DbUpdateException saveEx)
            {
                logger?.LogError(saveEx,
                    "Could not record the failure of programmatic completion for task {TaskId}; its lease will expire and it will be retried.",
                    state.TaskId);
                dbContext.Entry(state).State = EntityState.Detached;
            }

            if (terminal && ex is not TaskCompletionDispatchException)
                throw new TaskCompletionDispatchException(ex.Message, isTerminal: true, ex);

            throw;
        }
    }

    private async Task RunEffectsAsync(
        TaskItem task, ProgrammaticTaskCompletion state, CancellationToken cancellationToken)
    {
        if (state.NotificationsClearedAt is null)
        {
            await RemoveOpenTaskNotificationsAsync(task, cancellationToken);
            state.MarkNotificationsCleared(clock.UtcNowOffset());
            await SaveCheckpointAsync(state, cancellationToken);
        }

        if (state.CompletionNotificationAt is null)
        {
            if (task.AssignedEmployeeId is { } employeeId
                && !await notificationWriter.ExistsAsync(employeeId, task.Id, NotificationType.TaskCompleted, cancellationToken))
            {
                await notificationWriter.WriteAsync(
                    Guid.NewGuid(), task.CompanyId, employeeId,
                    $"Task completed: {task.Title}",
                    null,
                    task.Id,
                    NotificationType.TaskCompleted,
                    NotificationPriority.Normal,
                    task.CompletedAt ?? state.CreatedAt,
                    cancellationToken);
            }

            state.MarkCompletionNotificationWritten(clock.UtcNowOffset());
            await SaveCheckpointAsync(state, cancellationToken);
        }

        if (state.AuditPublishedAt is null)
        {
            await auditDelivery.EnsureDeliveredAsync(new TaskCompletedAuditEvent(
                task.CompanyId,
                task.Id,
                state.CompletedBy,
                state.PreviousStatus,
                task.AssignedEmployeeId,
                task.CompletedAt ?? state.CreatedAt), cancellationToken);

            state.MarkAuditPublished(clock.UtcNowOffset());
            await SaveCheckpointAsync(state, cancellationToken);
        }

        if (state.DispatchedAt is null)
        {
            var result = await dispatcher.DispatchAsync(new TaskCompletionContext(
                task.CompanyId,
                task.Id,
                task.Title,
                task.Description,
                task.Source,
                task.ActionType,
                task.AssignedEmployeeId,
                state.CompletedBy,
                task.CompletedAt ?? state.CreatedAt,
                task.SourceEntityId,
                DispatchOperationId: state.OperationId), cancellationToken);

            if (result.IsFailure)
            {
                throw new TaskCompletionDispatchException(
                    $"Completion action failed ({result.Error.Code}): {result.Error.Message}",
                    isTerminal: IsPermanent(result.Error));
            }

            state.MarkDispatched(clock.UtcNowOffset());
            await SaveCheckpointAsync(state, cancellationToken);
        }
    }

    private static bool IsPermanent(Error error) =>
        error.Code is "validation" or "not_found" or "forbidden" or "unauthorized";

    private async Task SaveCheckpointAsync(ProgrammaticTaskCompletion state, CancellationToken cancellationToken) =>
        await dbContext.ForceSaveChangesAdvancingVersionAsync(state, cancellationToken);

    private async Task RemoveOpenTaskNotificationsAsync(TaskItem task, CancellationToken cancellationToken)
    {
        foreach (var openTaskNotificationType in new[]
                 {
                     NotificationType.TaskAssigned,
                     NotificationType.TaskDueSoon,
                     NotificationType.TaskOverdue,
                 })
        {
            await notificationWriter.RemoveBySourceEntityAsync(
                task.CompanyId, task.Id, openTaskNotificationType, cancellationToken);
        }
    }
}
