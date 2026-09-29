using HR.SharedKernel;

namespace HR.Infrastructure.Abstractions;

public interface INotificationWriter
{
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
        CancellationToken cancellationToken = default,
        string? actionUrl = null);

    Task<bool> ExistsAsync(
        Guid employeeId,
        Guid sourceEntityId,
        NotificationType type,
        CancellationToken cancellationToken = default);

    Task<DateTimeOffset?> GetLastSentAtAsync(
        Guid employeeId,
        Guid sourceEntityId,
        NotificationType type,
        CancellationToken cancellationToken = default);

    Task<int> RemoveBySourceEntityAsync(
        Guid companyId,
        Guid sourceEntityId,
        NotificationType type,
        CancellationToken cancellationToken = default);
}
