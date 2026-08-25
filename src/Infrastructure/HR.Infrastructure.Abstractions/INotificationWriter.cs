using HR.SharedKernel;

namespace HR.Infrastructure.Abstractions;

public interface INotificationWriter
{
    /// <summary>
    /// NOT-03: template-based write path. Renders in-app title/body and (when the notification
    /// type is email-eligible) email subject/body from the notification-type's registered template
    /// using the supplied token values, validating every required token is present before anything
    /// is persisted. Returns a validation failure (nothing queued) if a required token is missing.
    /// Only the 6 initial template-backed types (see NotificationTemplateCatalogue) may be used
    /// here; every other type continues to use <see cref="WriteAsync"/> with a pre-formatted
    /// string.
    /// </summary>
    Task<Result> WriteTemplatedAsync(
        Guid id,
        Guid companyId,
        Guid employeeId,
        NotificationType type,
        IReadOnlyDictionary<string, string> tokens,
        Guid sourceEntityId,
        NotificationPriority priority,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default);

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
