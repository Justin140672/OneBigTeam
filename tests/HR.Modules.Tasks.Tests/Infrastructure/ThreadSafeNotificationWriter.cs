using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Tasks.Tests.Infrastructure;

internal sealed class ThreadSafeNotificationWriter : INotificationWriter
{
    private int _writes;

    public int Writes => _writes;

    public Task<Result> WriteTemplatedAsync(
        Guid id, Guid companyId, Guid employeeId, NotificationType type,
        IReadOnlyDictionary<string, string> tokens, Guid sourceEntityId,
        NotificationPriority priority, DateTimeOffset createdAt, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());

    public Task WriteAsync(
        Guid id, Guid companyId, Guid employeeId, string title, string? body, Guid sourceEntityId,
        NotificationType type, NotificationPriority priority, DateTimeOffset createdAt,
        CancellationToken cancellationToken = default, string? actionUrl = null)
    {
        Interlocked.Increment(ref _writes);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(
        Guid employeeId, Guid sourceEntityId, NotificationType type, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<DateTimeOffset?> GetLastSentAtAsync(
        Guid employeeId, Guid sourceEntityId, NotificationType type, CancellationToken cancellationToken = default) =>
        Task.FromResult<DateTimeOffset?>(null);

    public Task<int> RemoveBySourceEntityAsync(
        Guid companyId, Guid sourceEntityId, NotificationType type, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);
}
