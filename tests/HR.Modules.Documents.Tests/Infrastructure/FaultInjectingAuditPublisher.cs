using HR.SharedKernel;

namespace HR.Modules.Documents.Tests.Infrastructure;

internal sealed class FaultInjectingAuditPublisher : IAuditEventPublisher
{
    private readonly List<IAuditEvent> _published = [];

    public IReadOnlyList<IAuditEvent> Published => _published;
    public int FailNextPublishes { get; set; }

    public Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        if (FailNextPublishes > 0)
        {
            FailNextPublishes--;
            throw new InvalidOperationException("Simulated audit pipeline failure.");
        }

        if (auditEvent is IAuditEvent evt)
            _published.Add(evt);
        return Task.CompletedTask;
    }
}
