using HR.SharedKernel;

namespace HR.Modules.Companies.Tests.Infrastructure;

internal sealed class NoOpAuditEventPublisher : IAuditEventPublisher
{
    public Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

internal sealed class CapturingAuditEventPublisher : IAuditEventPublisher
{
    private readonly List<object> _published = [];
    public IReadOnlyList<object> Published => _published;

    public Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        _published.Add(auditEvent!);
        return Task.CompletedTask;
    }
}
