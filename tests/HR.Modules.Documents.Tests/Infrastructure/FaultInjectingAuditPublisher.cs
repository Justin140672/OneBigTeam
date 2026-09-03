using HR.SharedKernel;

namespace HR.Modules.Documents.Tests.Infrastructure;

/// <summary>
/// Records published audit events like <see cref="FakeAuditPublisher"/>, but throws on the next
/// <see cref="PublishAsync"/> call(s) when <see cref="FailNextPublishes"/> is set — used to
/// simulate a crash *after* a notification was written but *before* the run finished, so a
/// re-run can be proven not to send a second notification.
/// </summary>
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
