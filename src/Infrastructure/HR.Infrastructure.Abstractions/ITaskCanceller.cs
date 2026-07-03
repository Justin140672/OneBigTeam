namespace HR.Infrastructure.Abstractions;

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
}
