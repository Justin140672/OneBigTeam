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
            .Select(e => new AuditHistoryEntry(e.OccurredAt, e.EventType, e.EntityType, e.ActorUserId, e.ActorEmployeeId, e.Summary, e.BeforeJson, e.AfterJson, e.EmployeeId, e.EntityId, e.CorrelationId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditHistoryEntry>> GetRecentCompanyAuditHistoryAsync(
        Guid companyId, IReadOnlyCollection<string> entityTypes, int take, CancellationToken cancellationToken)
    {
        return await context.AuditEvents
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && entityTypes.Contains(e.EntityType))
            .OrderByDescending(e => e.OccurredAt)
            .Take(take)
            .Select(e => new AuditHistoryEntry(e.OccurredAt, e.EventType, e.EntityType, e.ActorUserId, e.ActorEmployeeId, e.Summary, e.BeforeJson, e.AfterJson, e.EmployeeId, e.EntityId, e.CorrelationId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditHistoryEntry>> GetEntityAuditHistoryAsync(
        Guid companyId, string entityType, Guid entityId, CancellationToken cancellationToken)
    {
        return await context.AuditEvents
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.EntityType == entityType && e.EntityId == entityId)
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => new AuditHistoryEntry(e.OccurredAt, e.EventType, e.EntityType, e.ActorUserId, e.ActorEmployeeId, e.Summary, e.BeforeJson, e.AfterJson, e.EmployeeId, e.EntityId, e.CorrelationId))
            .ToListAsync(cancellationToken);
    }
}
