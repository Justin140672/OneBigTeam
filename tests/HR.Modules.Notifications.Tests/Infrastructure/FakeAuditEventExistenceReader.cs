using HR.Infrastructure.Abstractions;

namespace HR.Modules.Notifications.Tests.Infrastructure;

internal sealed class FakeAuditEventExistenceReader : IAuditEventExistenceReader
{
    private readonly HashSet<Guid> _existingEventIds;

    public FakeAuditEventExistenceReader(IEnumerable<Guid>? existingEventIds = null)
    {
        _existingEventIds = existingEventIds is null ? [] : [.. existingEventIds];
    }

    public List<Guid> Queried { get; } = [];

    public Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        Queried.Add(eventId);
        return Task.FromResult(_existingEventIds.Contains(eventId));
    }
}
