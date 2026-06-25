namespace HR.Modules.Notifications.Contracts;

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
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Guid employeeId,
        Guid sourceEntityId,
        NotificationType type,
        CancellationToken cancellationToken = default);
}
