using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Support.Tests.Infrastructure;

internal sealed class FakeNotificationWriter : INotificationWriter
{
    public List<(Guid CompanyId, Guid EmployeeId, NotificationType Type, Guid SourceEntityId)> WrittenNotifications { get; } = [];

    public Task WriteAsync(
        Guid id, Guid companyId, Guid employeeId, string title, string? body, Guid sourceEntityId,
        NotificationType type, NotificationPriority priority, DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        WrittenNotifications.Add((companyId, employeeId, type, sourceEntityId));
        return Task.CompletedTask;
    }

    /// <summary>NOT-03: no Support handler currently raises a template-backed NotificationType, so
    /// this fake records a generic entry rather than reproducing the real catalogue's wording.</summary>
    public Task<Result> WriteTemplatedAsync(
        Guid id, Guid companyId, Guid employeeId, NotificationType type,
        IReadOnlyDictionary<string, string> tokens, Guid sourceEntityId,
        NotificationPriority priority, DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        WrittenNotifications.Add((companyId, employeeId, type, sourceEntityId));
        return Task.FromResult(Result.Success());
    }

    public Task<bool> ExistsAsync(Guid employeeId, Guid sourceEntityId, NotificationType type, CancellationToken cancellationToken = default) =>
        Task.FromResult(WrittenNotifications.Any(n => n.EmployeeId == employeeId && n.SourceEntityId == sourceEntityId && n.Type == type));

    public Task<DateTimeOffset?> GetLastSentAtAsync(Guid employeeId, Guid sourceEntityId, NotificationType type, CancellationToken cancellationToken = default) =>
        Task.FromResult<DateTimeOffset?>(null);

    public Task<int> RemoveBySourceEntityAsync(Guid companyId, Guid sourceEntityId, NotificationType type, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);
}
