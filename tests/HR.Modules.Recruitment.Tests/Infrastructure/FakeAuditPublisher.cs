using HR.SharedKernel;

namespace HR.Modules.Recruitment.Tests.Infrastructure;

internal sealed class FakeAuditPublisher : IAuditEventPublisher
{
    private readonly List<IAuditEvent> _published = [];

    public IReadOnlyList<IAuditEvent> Published => _published;

    public Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        if (auditEvent is IAuditEvent evt)
            _published.Add(evt);
        return Task.CompletedTask;
    }
}
