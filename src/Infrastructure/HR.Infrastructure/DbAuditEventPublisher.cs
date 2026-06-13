using HR.Infrastructure.Persistence;
using HR.SharedKernel;

namespace HR.Infrastructure;

internal sealed class DbAuditEventPublisher(AuditDbContext context) : IAuditEventPublisher
{
    public async Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        if (auditEvent is IAuditEvent evt)
        {
            context.AuditEvents.Add(AuditEvent.From(evt));
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
