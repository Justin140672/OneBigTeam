using HR.Infrastructure.Abstractions;

namespace HR.Modules.Tasks.Services;

internal sealed class TaskCompletionDispatcher(IEnumerable<ITaskCompletionAction> actions)
{
    public async Task DispatchAsync(TaskCompletionContext context, CancellationToken cancellationToken)
    {
        foreach (var action in actions.Where(a => a.Source == context.Source && a.ActionType == context.ActionType))
            await action.ExecuteAsync(context, cancellationToken);
    }
}
