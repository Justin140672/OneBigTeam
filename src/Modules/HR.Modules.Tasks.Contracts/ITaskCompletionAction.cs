using HR.SharedKernel;

namespace HR.Modules.Tasks.Contracts;

/// <summary>
/// Implement this interface in any module to react when a task of a given
/// <see cref="TaskSource"/> is completed. Register as
/// <c>services.AddScoped&lt;ITaskCompletionAction, YourAction&gt;()</c>
/// in your module's DI setup; the Tasks module dispatcher will invoke all
/// registered implementations whose <see cref="Source"/> matches — BEFORE the underlying
/// TaskItem is actually marked Completed (see CompleteTaskHandler). A failed <see cref="Result"/>
/// aborts completion entirely: the task stays in its current (actionable) status and the caller
/// receives the failure. Implementations that have no required outcome/decision to validate
/// (e.g. a plain review/acknowledgement with no structured payload) should return
/// <see cref="Result.Success()"/> for the "nothing to validate" case — only return a failure when
/// the action genuinely could not be carried out.
/// </summary>
public interface ITaskCompletionAction
{
    /// <summary>The task source this action handles.</summary>
    TaskSource Source { get; }

    /// <summary>The action type this implementation handles.</summary>
    TaskActionType ActionType { get; }

    Task<Result> ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken);
}
