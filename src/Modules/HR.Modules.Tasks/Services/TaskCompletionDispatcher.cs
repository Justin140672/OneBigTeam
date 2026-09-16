using HR.Modules.Tasks.Contracts;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Tasks.Services;

internal sealed class TaskCompletionDispatcher(IEnumerable<ITaskCompletionAction> actions)
{
    // Ticket 3 (P1): runs BEFORE the underlying task is marked Completed (see CompleteTaskHandler)
    // so a rejected/invalid outcome aborts completion instead of silently completing the task with
    // no matching business-state change. Stops at the first failing action rather than running the
    // rest — there is normally exactly one matching action per (Source, ActionType) pair.
    public async Task<Result> DispatchAsync(TaskCompletionContext context, CancellationToken cancellationToken)
    {
        foreach (var action in actions.Where(a => a.Source == context.Source && a.ActionType == context.ActionType))
        {
            var result = await action.ExecuteAsync(context, cancellationToken);
            if (!result.IsSuccess)
                return result;
        }

        return Result.Success();
    }
}
