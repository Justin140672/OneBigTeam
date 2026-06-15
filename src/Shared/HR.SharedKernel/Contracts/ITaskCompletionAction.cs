namespace HR.SharedKernel.Contracts;

/// <summary>
/// Implement this interface in any module to react when a task of a given
/// <see cref="TaskSource"/> is completed. Register as
/// <c>services.AddScoped&lt;ITaskCompletionAction, YourAction&gt;()</c>
/// in your module's DI setup; the Tasks module dispatcher will invoke all
/// registered implementations whose <see cref="Source"/> matches.
/// </summary>
public interface ITaskCompletionAction
{
    /// <summary>The task source this action handles.</summary>
    TaskSource Source { get; }

    Task ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken);
}
