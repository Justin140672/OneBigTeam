namespace HR.Infrastructure.Abstractions;

public interface INotificationWriter
{
    Task WriteAsync(
        Guid id,
        Guid companyId,
        Guid employeeId,
        string title,
        string? body,
        Guid sourceEntityId,
        NotificationType type,
        NotificationPriority priority,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Guid employeeId,
        Guid sourceEntityId,
        NotificationType type,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the most recent matching notification's CreatedAt, or null if none exists.</summary>
    Task<DateTimeOffset?> GetLastSentAtAsync(
        Guid employeeId,
        Guid sourceEntityId,
        NotificationType type,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every notification matching the given company, source entity and type — used when a
    /// withdrawal/cancellation action means previously-sent in-app notifications are no longer
    /// relevant. Returns the number of notifications removed.
    /// </summary>
    Task<int> RemoveBySourceEntityAsync(
        Guid companyId,
        Guid sourceEntityId,
        NotificationType type,
        CancellationToken cancellationToken = default);
}
