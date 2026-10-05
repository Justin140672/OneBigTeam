using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

namespace HR.Modules.Tasks.Tests.Infrastructure;

/// <summary>
/// Models the real publisher/reader pair: <see cref="SwallowPersistenceFailure"/> reproduces
/// DbAuditEventPublisher's log-and-swallow behaviour (returns normally, persists nothing), and
/// <see cref="ExistsAsync"/> only sees events that were actually persisted.
/// </summary>
internal sealed class FakeAuditPublisher : IAuditEventPublisher, IAuditEventExistenceReader
{
    private readonly List<IAuditEvent> _published = [];
    private readonly object _gate = new();

    public IReadOnlyList<IAuditEvent> Published
    {
        get { lock (_gate) return _published.ToList(); }
    }

    public int PublishCalls { get; private set; }

    /// <summary>
    /// Ticket 4 (P1): when set, PublishAsync throws instead of recording, simulating a transient
    /// audit-publish failure so CompleteTaskHandler's inline-failure -> TaskCompletionEffectsJob
    /// handoff path can be exercised.
    /// </summary>
    public bool ThrowOnPublish { get; set; }

    public bool SwallowPersistenceFailure { get; set; }

    /// <summary>Overrides the existence answer (null = report what was persisted).</summary>
    public bool? ExistsOverride { get; set; }

    public Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        lock (_gate)
            PublishCalls++;

        if (ThrowOnPublish)
            throw new InvalidOperationException("Simulated audit publish failure.");

        if (SwallowPersistenceFailure)
            return Task.CompletedTask;

        if (auditEvent is IAuditEvent evt)
        {
            lock (_gate)
            {
                _published.Add(evt);
            }
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        if (ExistsOverride is { } forced)
            return Task.FromResult(forced);

        lock (_gate)
            return Task.FromResult(_published.Any(e => e.EventId == eventId));
    }

    public void Seed(IAuditEvent auditEvent)
    {
        lock (_gate)
            _published.Add(auditEvent);
    }
}
