using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Documents.Tests.Infrastructure;

/// <summary>
/// Wraps <see cref="FakeNotificationWriter"/> and throws on a configurable number of the next
/// <see cref="WriteAsync"/> calls, to simulate a partial-failure mid-run. All other members
/// delegate straight through, so a re-run after the fault sees whatever was already recorded.
/// </summary>
internal sealed class FaultInjectingNotificationWriter(FakeNotificationWriter inner) : INotificationWriter
{
    public int FailNextWrites { get; set; }
    public int WriteAttempts { get; private set; }

    public FakeNotificationWriter Inner => inner;

    public Task WriteAsync(
        Guid id, Guid companyId, Guid employeeId,
        string title, string? body,
        Guid sourceEntityId, NotificationType type,
        NotificationPriority priority,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        WriteAttempts++;
        if (FailNextWrites > 0)
        {
            FailNextWrites--;
            throw new InvalidOperationException("Simulated notification store failure.");
        }

        return inner.WriteAsync(id, companyId, employeeId, title, body, sourceEntityId, type, priority, createdAt, cancellationToken);
    }

    public Task<Result> WriteTemplatedAsync(
        Guid id, Guid companyId, Guid employeeId, NotificationType type,
        IReadOnlyDictionary<string, string> tokens, Guid sourceEntityId,
        NotificationPriority priority, DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
        => inner.WriteTemplatedAsync(id, companyId, employeeId, type, tokens, sourceEntityId, priority, createdAt, cancellationToken);

    public Task<bool> ExistsAsync(Guid employeeId, Guid sourceEntityId, NotificationType type, CancellationToken cancellationToken = default)
        => inner.ExistsAsync(employeeId, sourceEntityId, type, cancellationToken);

    public Task<DateTimeOffset?> GetLastSentAtAsync(Guid employeeId, Guid sourceEntityId, NotificationType type, CancellationToken cancellationToken = default)
        => inner.GetLastSentAtAsync(employeeId, sourceEntityId, type, cancellationToken);

    public Task<int> RemoveBySourceEntityAsync(Guid companyId, Guid sourceEntityId, NotificationType type, CancellationToken cancellationToken = default)
        => inner.RemoveBySourceEntityAsync(companyId, sourceEntityId, type, cancellationToken);
}
