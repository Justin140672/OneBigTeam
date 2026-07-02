using HR.SharedKernel;

namespace HR.Modules.Sickness.Tests.Infrastructure;

internal sealed class FakeAuditEventPublisher : IAuditEventPublisher
{
    public List<object> PublishedEvents { get; } = [];

    public Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        PublishedEvents.Add(auditEvent!);
        return Task.CompletedTask;
    }
}
