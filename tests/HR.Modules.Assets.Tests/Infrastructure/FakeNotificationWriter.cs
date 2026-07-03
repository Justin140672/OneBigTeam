using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Assets.Tests.Infrastructure;

internal sealed class FakeNotificationWriter : INotificationWriter
{
    private readonly List<WrittenNotification> _written = [];

    public IReadOnlyList<WrittenNotification> Written => _written;

    public Task WriteAsync(
        Guid id,
        Guid companyId,
        Guid employeeId,
        string title,
        string? body,
        Guid sourceEntityId,
        NotificationType type,
        NotificationPriority priority,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        _written.Add(new WrittenNotification(
            id, companyId, employeeId, title, body, sourceEntityId, type, priority, createdAt));
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(
        Guid employeeId,
        Guid sourceEntityId,
        NotificationType type,
        CancellationToken cancellationToken = default)
    {
        var exists = _written.Any(n =>
            n.EmployeeId == employeeId &&
            n.SourceEntityId == sourceEntityId &&
            n.Type == type);
        return Task.FromResult(exists);
    }

    internal sealed record WrittenNotification(
        Guid Id,
        Guid CompanyId,
        Guid EmployeeId,
        string Title,
        string? Body,
        Guid SourceEntityId,
        NotificationType Type,
        NotificationPriority Priority,
        DateTimeOffset CreatedAt);
}
