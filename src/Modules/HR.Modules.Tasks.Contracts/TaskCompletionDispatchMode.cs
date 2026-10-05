namespace HR.Modules.Tasks.Contracts;

public enum TaskCompletionDispatchMode
{
    /// <summary>Run the task's completion action as part of completing it.</summary>
    Dispatch = 0,

    /// <summary>
    /// The originating operation already applied the business effect the completion action would
    /// apply, so dispatch is recorded as applied rather than re-run. Every other completion effect
    /// (notifications, audit, confirmation) still runs.
    /// </summary>
    BusinessEffectAlreadyApplied = 1,
}
