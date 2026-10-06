using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Tasks.Features.CompleteTask.Actions;

internal sealed class InternalOfferTaskCompletionAction : ITaskCompletionAction
{
    public TaskSource Source => TaskSource.Recruitment;
    public TaskActionType ActionType => TaskActionType.Approve;

    public Task<Result> ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure(Error.Validation(
            "Open the offer to accept or decline it. This task closes automatically when you respond.")));
}
