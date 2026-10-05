using HR.Infrastructure.Abstractions;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Tasks.Services;

internal sealed class TaskCompletionRecovery(
    TasksDbContext dbContext,
    IClock clock,
    IAuditEventPublisher auditPublisher,
    ILogger<TaskCompletionRecovery> logger) : ITaskCompletionRecovery
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

        if (state is null)
            return new TaskCompletionResetResult(TaskCompletionResetOutcome.NotFound, operationId);

        if (!state.IsTerminallyFailed)
            return new TaskCompletionResetResult(TaskCompletionResetOutcome.NotTerminal, operationId, state.TaskId);

        var expectedVersion = state.Version;
        var attemptsBeforeReset = state.AttemptCount;
        var now = clock.UtcNowOffset();
        state.ResetTerminalFailure(operatorUserId, now);

        var save = await dbContext.SaveChangesWithConcurrencyAsync(
            state, expectedVersion, "This programmatic task completion was changed by another request.", cancellationToken);

        if (save.IsFailure)
        {
            var taskId = state.TaskId;
            dbContext.Entry(state).State = EntityState.Detached;

            var stillTerminal = await dbContext.ProgrammaticTaskCompletions.AsNoTracking()
                .AnyAsync(c => c.OperationId == operationId && c.TerminalFailureAt != null, cancellationToken);

            return new TaskCompletionResetResult(
                stillTerminal ? TaskCompletionResetOutcome.Conflict : TaskCompletionResetOutcome.NotTerminal,
                operationId, taskId);
        }

        logger.LogWarning(
            "Terminal programmatic task completion reset by operator. CompanyId={CompanyId} TaskId={TaskId} TasksOperationId={TasksOperationId} OperatorUserId={OperatorUserId} PreviousAttempts={PreviousAttempts} ResetCount={ResetCount}",
            state.CompanyId, state.TaskId, state.OperationId, operatorUserId, attemptsBeforeReset, state.ResetCount);

        await auditPublisher.PublishAsync(
            new ProgrammaticTaskCompletionResetAuditEvent(
                state.CompanyId, state.TaskId, state.OperationId, operatorUserId, reason, now),
            cancellationToken);

        return new TaskCompletionResetResult(TaskCompletionResetOutcome.Reset, operationId, state.TaskId);
    }
}
