using System.Collections.Concurrent;
using HR.SharedKernel;

namespace HR.Integration.Tests.Infrastructure;

/// <summary>
/// Test-armable switchboard for the audit persistence boundary. An armed event id reproduces the
/// production publisher's log-and-swallow behaviour: PublishAsync returns normally and persists
/// nothing. Everything else passes straight through to the real publisher.
/// </summary>
internal sealed class AuditFaultInjector
{
    private readonly ConcurrentDictionary<Guid, byte> _dropped = new();
    private readonly ConcurrentDictionary<Guid, int> _attempts = new();

    public void Drop(params Guid[] eventIds)
    {
        foreach (var id in eventIds)
            _dropped[id] = 0;
    }

    public void Restore(params Guid[] eventIds)
    {
        foreach (var id in eventIds)
            _dropped.TryRemove(id, out _);
    }

    public bool ShouldDrop(Guid eventId) => _dropped.ContainsKey(eventId);

    public int PublishAttempts(Guid eventId) => _attempts.GetValueOrDefault(eventId);

    public void RecordAttempt(Guid eventId) => _attempts.AddOrUpdate(eventId, 1, (_, n) => n + 1);
}

internal sealed class FaultInjectingAuditPublisher(IAuditEventPublisher inner, AuditFaultInjector injector) : IAuditEventPublisher
{
    public Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        if (auditEvent is IAuditEvent evt)
        {
            injector.RecordAttempt(evt.EventId);

            if (injector.ShouldDrop(evt.EventId))
                return Task.CompletedTask;
        }

        return inner.PublishAsync(auditEvent, cancellationToken);
    }
}
