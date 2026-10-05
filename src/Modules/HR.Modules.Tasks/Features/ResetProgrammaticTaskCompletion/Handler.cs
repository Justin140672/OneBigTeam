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
                Error.NotFound($"Programmatic task completion '{request.OperationId}' was not found.")),
            TaskCompletionResetOutcome.Conflict => Result.Failure<ResetProgrammaticTaskCompletionResponse>(
                Error.Concurrency("This programmatic task completion was changed by another request. Reload and try again.")),
            _ => Result.Success(new ResetProgrammaticTaskCompletionResponse(
                result.OperationId, result.TaskId, result.Outcome == TaskCompletionResetOutcome.Reset)),
        };
    }
}
