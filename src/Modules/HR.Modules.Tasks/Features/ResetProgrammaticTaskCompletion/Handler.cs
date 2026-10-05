using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Tasks.Features.ResetProgrammaticTaskCompletion;

internal sealed class ResetProgrammaticTaskCompletionHandler(ITaskCompletionRecovery recovery)
{
    public async Task<Result<ResetProgrammaticTaskCompletionResponse>> HandleAsync(
        ResetProgrammaticTaskCompletionRequest request,
        Guid operatorUserId,
        CancellationToken cancellationToken)
    {
        var result = await recovery.ResetTerminalCompletionAsync(
            request.CompanyId, request.OperationId, operatorUserId, request.Reason.Trim(), cancellationToken);

        return result.Outcome switch
        {
            TaskCompletionResetOutcome.NotFound => Result.Failure<ResetProgrammaticTaskCompletionResponse>(
                Error.NotFound($"Task completion operation '{request.OperationId}' was not found.")),
            TaskCompletionResetOutcome.Conflict => Result.Failure<ResetProgrammaticTaskCompletionResponse>(
                Error.Concurrency("This task completion operation was changed by another request. Reload and try again.")),
            TaskCompletionResetOutcome.DataIntegrityFailure => Result.Failure<ResetProgrammaticTaskCompletionResponse>(
                Error.Conflict($"Task completion operation '{request.OperationId}' is a data-integrity failure and needs investigation; it cannot be reset.")),
            _ => Result.Success(new ResetProgrammaticTaskCompletionResponse(
                result.OperationId, result.TaskId, result.Outcome == TaskCompletionResetOutcome.Reset,
                result.RecoveryActionId, result.ResetCount, result.OperationKind)),
        };
    }
}
