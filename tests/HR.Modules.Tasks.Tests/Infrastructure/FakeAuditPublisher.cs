using HR.SharedKernel;

namespace HR.Modules.Tasks.Tests.Infrastructure;

internal sealed class FakeAuditPublisher : IAuditEventPublisher
{
    private readonly List<IAuditEvent> _published = [];

    public IReadOnlyList<IAuditEvent> Published => _published;

    /// <summary>
    /// Ticket 4 (P1): when set, PublishAsync throws instead of recording, simulating a transient
    /// audit-publish failure so CompleteTaskHandler's inline-failure -> TaskCompletionEffectsJob
    /// handoff path can be exercised.
    /// </summary>
    public bool ThrowOnPublish { get; set; }

    public Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        if (ThrowOnPublish)
            throw new InvalidOperationException("Simulated audit publish failure.");

        if (auditEvent is IAuditEvent evt)
            _published.Add(evt);
        return Task.CompletedTask;
    }
}
