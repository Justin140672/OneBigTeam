using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Documents.Tests.Infrastructure;

internal sealed class FakeNotificationWriter : INotificationWriter
{
    public record WrittenNotification(
        Guid Id, Guid CompanyId, Guid EmployeeId,
        string Title, string? Body,
        Guid SourceEntityId, NotificationType Type,
        NotificationPriority Priority, DateTimeOffset CreatedAt);

    public List<WrittenNotification> Written { get; } = [];

    public Task WriteAsync(
        Guid id, Guid companyId, Guid employeeId,
        string title, string? body,
        Guid sourceEntityId, NotificationType type,
        NotificationPriority priority,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        Written.Add(new WrittenNotification(id, companyId, employeeId, title, body, sourceEntityId, type, priority, createdAt));
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(
        Guid employeeId, Guid sourceEntityId, NotificationType type,
        CancellationToken cancellationToken = default)
    {
        var exists = Written.Any(n =>
            n.EmployeeId == employeeId &&
            n.SourceEntityId == sourceEntityId &&
            n.Type == type);
        return Task.FromResult(exists);
    }

    public Task<DateTimeOffset?> GetLastSentAtAsync(
        Guid employeeId, Guid sourceEntityId, NotificationType type,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Written
            .Where(n => n.EmployeeId == employeeId && n.SourceEntityId == sourceEntityId && n.Type == type)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => (DateTimeOffset?)n.CreatedAt)
            .FirstOrDefault());

    public Task<int> RemoveBySourceEntityAsync(
        Guid companyId, Guid sourceEntityId, NotificationType type,
        CancellationToken cancellationToken = default)
    {
        var removed = Written.RemoveAll(n =>
            n.CompanyId == companyId && n.SourceEntityId == sourceEntityId && n.Type == type);
        return Task.FromResult(removed);
    }
}

internal sealed class NoOpNotificationWriter : INotificationWriter
{
    public Task WriteAsync(
        Guid id, Guid companyId, Guid employeeId,
        string title, string? body,
        Guid sourceEntityId, NotificationType type,
        NotificationPriority priority,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<bool> ExistsAsync(
        Guid employeeId, Guid sourceEntityId, NotificationType type,
        CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<DateTimeOffset?> GetLastSentAtAsync(
        Guid employeeId, Guid sourceEntityId, NotificationType type,
        CancellationToken cancellationToken = default)
        => Task.FromResult<DateTimeOffset?>(null);

    public Task<int> RemoveBySourceEntityAsync(
        Guid companyId, Guid sourceEntityId, NotificationType type,
        CancellationToken cancellationToken = default)
        => Task.FromResult(0);
}
