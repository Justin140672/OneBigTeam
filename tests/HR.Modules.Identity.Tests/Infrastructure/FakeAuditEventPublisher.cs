using HR.SharedKernel;

namespace HR.Modules.Identity.Tests.Infrastructure;

internal sealed class FakeAuditEventPublisher : IAuditEventPublisher
{
    public List<object> PublishedEvents { get; } = [];

    public Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        PublishedEvents.Add(auditEvent!);
        return Task.CompletedTask;
    }
}
