using Hangfire;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using HR.Modules.Tasks.Jobs;
using HR.Modules.Tasks.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Tasks.Services;

internal sealed class TaskCompletionRecovery(
    TasksDbContext dbContext,
    IClock clock,
    TaskRecoveryAuditDelivery auditDelivery,
    ILogger<TaskCompletionRecovery> logger,
    IExecutionContextAccessor? executionContextAccessor = null,
    IBackgroundJobClient? backgroundJobClient = null) : ITaskCompletionRecovery
{
    public async Task<TaskCompletionResetResult> ResetTerminalCompletionAsync(
        Guid companyId,
        Guid operationId,
        Guid operatorUserId,
        string reason,
        CancellationToken cancellationToken)
    {
        var state = await dbContext.ProgrammaticTaskCompletions
            .SingleOrDefaultAsync(c => c.OperationId == operationId && c.CompanyId == companyId, cancellationToken);

        if (state is not null)
            return await ResetProgrammaticAsync(state, operatorUserId, reason, cancellationToken);

        var operation = await dbContext.TaskCompletionOperations
            .SingleOrDefaultAsync(o => o.Id == operationId && o.CompanyId == companyId, cancellationToken);

        if (operation is null)
            return new TaskCompletionResetResult(TaskCompletionResetOutcome.NotFound, operationId);

        return await ResetInteractiveAsync(operation, operatorUserId, reason, cancellationToken);
    }

    private async Task<TaskCompletionResetResult> ResetProgrammaticAsync(
        ProgrammaticTaskCompletion state, Guid operatorUserId, string reason, CancellationToken cancellationToken)
    {
        var operationId = state.OperationId;

        if (!state.IsTerminallyFailed)
        {
            return new TaskCompletionResetResult(
                TaskCompletionResetOutcome.NotTerminal, operationId, state.TaskId,
                OperationKind: TaskRecoveryAction.KindProgrammatic);
        }

        var expectedVersion = state.Version;
        var attemptsBeforeReset = state.AttemptCount;
        var now = clock.UtcNowOffset();
        state.ResetTerminalFailure(operatorUserId, now);

        var action = NewAction(
            state.CompanyId, state.TaskId, operationId, TaskRecoveryAction.KindProgrammatic,
            operatorUserId, reason, state.ResetCount, now);
        dbContext.TaskRecoveryActions.Add(action);

        var saved = await TrySaveResetAsync(
            state, expectedVersion, "This programmatic task completion was changed by another request.", action.Id, cancellationToken);

        if (!saved)
        {
            var taskId = state.TaskId;
            dbContext.Entry(action).State = EntityState.Detached;
            dbContext.Entry(state).State = EntityState.Detached;

            var stillTerminal = await dbContext.ProgrammaticTaskCompletions.AsNoTracking()
                .AnyAsync(c => c.OperationId == operationId && c.TerminalFailureAt != null, cancellationToken);

            return new TaskCompletionResetResult(
                stillTerminal ? TaskCompletionResetOutcome.Conflict : TaskCompletionResetOutcome.NotTerminal,
                operationId, taskId, OperationKind: TaskRecoveryAction.KindProgrammatic);
        }

        logger.LogWarning(
            "Terminal programmatic task completion reset by operator. CompanyId={CompanyId} TaskId={TaskId} TasksOperationId={TasksOperationId} RecoveryActionId={RecoveryActionId} OperatorUserId={OperatorUserId} PreviousAttempts={PreviousAttempts} ResetCount={ResetCount} CorrelationId={CorrelationId}",
            state.CompanyId, state.TaskId, operationId, action.Id, operatorUserId, attemptsBeforeReset, state.ResetCount, action.CorrelationId);

        await TryDeliverAuditAsync(action, cancellationToken);

        return new TaskCompletionResetResult(
            TaskCompletionResetOutcome.Reset, operationId, state.TaskId, action.Id, state.ResetCount,
            TaskRecoveryAction.KindProgrammatic);
    }

    private async Task<TaskCompletionResetResult> ResetInteractiveAsync(
        TaskCompletionOperation operation, Guid operatorUserId, string reason, CancellationToken cancellationToken)
    {
        var operationId = operation.Id;

        if (operation.Status == TaskCompletionOperation.StatusDataIntegrityFailure)
        {
            return new TaskCompletionResetResult(
                TaskCompletionResetOutcome.DataIntegrityFailure, operationId, operation.TaskId,
                OperationKind: TaskRecoveryAction.KindInteractive);
        }

        if (operation.Status != TaskCompletionOperation.StatusEffectsTerminalFailure)
        {
            return new TaskCompletionResetResult(
                TaskCompletionResetOutcome.NotTerminal, operationId, operation.TaskId,
                OperationKind: TaskRecoveryAction.KindInteractive);
        }

        var expectedVersion = operation.Version;
        var attemptsBeforeReset = operation.AttemptCount;
        var now = clock.UtcNowOffset();
        operation.ResetEffectsTerminalFailure(operatorUserId, now);

        var action = NewAction(
            operation.CompanyId, operation.TaskId, operationId, TaskRecoveryAction.KindInteractive,
            operatorUserId, reason, operation.ResetCount, now);
        dbContext.TaskRecoveryActions.Add(action);

        var saved = await TrySaveResetAsync(
            operation, expectedVersion, "This task completion operation was changed by another request.", action.Id, cancellationToken);

        if (!saved)
        {
            var taskId = operation.TaskId;
            dbContext.Entry(action).State = EntityState.Detached;
            dbContext.Entry(operation).State = EntityState.Detached;

            var stillTerminal = await dbContext.TaskCompletionOperations.AsNoTracking()
                .AnyAsync(o => o.Id == operationId && o.Status == TaskCompletionOperation.StatusEffectsTerminalFailure, cancellationToken);

            return new TaskCompletionResetResult(
                stillTerminal ? TaskCompletionResetOutcome.Conflict : TaskCompletionResetOutcome.NotTerminal,
                operationId, taskId, OperationKind: TaskRecoveryAction.KindInteractive);
        }

        logger.LogWarning(
            "Terminal interactive task completion effects reset by operator. CompanyId={CompanyId} TaskId={TaskId} TasksOperationId={TasksOperationId} RecoveryActionId={RecoveryActionId} OperatorUserId={OperatorUserId} PreviousAttempts={PreviousAttempts} ResetCount={ResetCount} CorrelationId={CorrelationId}",
            operation.CompanyId, operation.TaskId, operationId, action.Id, operatorUserId, attemptsBeforeReset, operation.ResetCount, action.CorrelationId);

        try
        {
            backgroundJobClient?.Enqueue<TaskCompletionEffectsJob>(
                job => job.ProcessAsync(operationId, operation.CompanyId));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not enqueue the effects job after reset; the completion reconciliation sweep will pick it up. TasksOperationId={TasksOperationId}",
                operationId);
        }

        await TryDeliverAuditAsync(action, cancellationToken);

        return new TaskCompletionResetResult(
            TaskCompletionResetOutcome.Reset, operationId, operation.TaskId, action.Id, operation.ResetCount,
            TaskRecoveryAction.KindInteractive);
    }

    private async Task<bool> TrySaveResetAsync<T>(
        T aggregate, int expectedVersion, string conflictMessage, Guid actionId, CancellationToken cancellationToken)
        where T : class, IVersionedAggregate
    {
        try
        {
            return (await dbContext.SaveChangesWithConcurrencyAsync(
                aggregate, expectedVersion, conflictMessage, cancellationToken)).IsSuccess;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(aggregate).State = EntityState.Detached;

            if (await dbContext.TaskRecoveryActions.AsNoTracking().AnyAsync(a => a.Id == actionId, CancellationToken.None))
                return false;

            throw;
        }
    }

    private TaskRecoveryAction NewAction(
        Guid companyId, Guid taskId, Guid operationId, string kind, Guid operatorUserId,
        string reason, int sequenceNumber, DateTimeOffset now)
    {
        var correlation = executionContextAccessor?.Current is { } context
            ? CorrelationIdGuid.Derive(context.CorrelationId)
            : (Guid?)null;

        return TaskRecoveryAction.Create(
            companyId, taskId, operationId, kind, operatorUserId,
            reason.Length > 500 ? reason[..500] : reason, sequenceNumber, now, correlation);
    }

    private async Task TryDeliverAuditAsync(TaskRecoveryAction action, CancellationToken cancellationToken)
    {
        try
        {
            await auditDelivery.DeliverAsync(action, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Immediate audit delivery for task recovery action {RecoveryActionId} failed; the durable intent remains outstanding for the background sweep. CompanyId={CompanyId} TasksOperationId={TasksOperationId}",
                action.Id, action.CompanyId, action.OperationId);
        }
    }
}
