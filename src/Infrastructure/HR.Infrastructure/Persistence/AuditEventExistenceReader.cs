using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Infrastructure.Persistence;

internal sealed class AuditEventExistenceReader(AuditDbContext db) : IAuditEventExistenceReader
{
    public Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        db.AuditEvents.AsNoTracking().AnyAsync(e => e.EventId == eventId, cancellationToken);
}
