namespace HR.Modules.Tasks.Contracts;

public interface ITaskCanceller
{
    /// <summary>
    /// Finds the first open task linked to <paramref name="sourceEntityId"/> and cancels it.
    /// No-op if no matching task exists or it is already completed/cancelled.
    /// </summary>
    Task CancelBySourceEntityAsync(
        Guid companyId,
        Guid sourceEntityId,
        TaskSource source,
        TaskActionType actionType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Cancels every open task linked to <paramref name="sourceEntityId"/> — unlike
    /// <see cref="CancelBySourceEntityAsync"/>, which only cancels the first match, this is for
    /// source entities that fan out to one task per recipient (e.g. one Acknowledge task per
    /// eligible employee on a shared document). Returns the number of tasks cancelled.
    /// </summary>
    Task<int> CancelAllBySourceEntityAsync(
        Guid companyId,
        Guid sourceEntityId,
        TaskSource source,
        TaskActionType actionType,
        CancellationToken cancellationToken);
}
