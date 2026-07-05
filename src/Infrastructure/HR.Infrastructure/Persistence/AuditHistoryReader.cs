using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Infrastructure.Persistence;

internal sealed class AuditHistoryReader(AuditDbContext context) : IAuditHistoryReader
{
    public async Task<IReadOnlyList<AuditHistoryEntry>> GetEmployeeAuditHistoryAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken)
    {
        return await context.AuditEvents
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.EmployeeId == employeeId)
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => new AuditHistoryEntry(e.OccurredAt, e.EventType, e.EntityType, e.ActorUserId, e.ActorEmployeeId, e.Summary, e.BeforeJson, e.AfterJson))
            .ToListAsync(cancellationToken);
    }
}
