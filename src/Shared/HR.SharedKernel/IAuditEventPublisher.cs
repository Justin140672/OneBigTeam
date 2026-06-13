namespace HR.SharedKernel;

public interface IAuditEventPublisher
{
    Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken);
}
