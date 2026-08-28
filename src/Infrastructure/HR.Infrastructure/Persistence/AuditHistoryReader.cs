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

    public async Task<HR.SharedKernel.PagedResult<AuditHistoryEntry>> GetCompanyAuditLogAsync(
        Guid companyId,
        Guid? employeeId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        string? eventType,
        HR.SharedKernel.Pagination pagination,
        CancellationToken cancellationToken)
    {
        // companyId is always applied — this method must never return rows from another tenant.
        var query = context.AuditEvents
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId);

        if (employeeId.HasValue)
            query = query.Where(e => e.EmployeeId == employeeId.Value);

        if (fromDate.HasValue)
            query = query.Where(e => e.OccurredAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(e => e.OccurredAt <= toDate.Value);

        if (!string.IsNullOrWhiteSpace(eventType))
            query = query.Where(e => e.EventType == eventType);

        query = query.OrderByDescending(e => e.OccurredAt);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip(pagination.Offset)
            .Take(pagination.PageSize)
            .Select(e => new AuditHistoryEntry(
                e.OccurredAt, e.EventType, e.EntityType, e.ActorUserId, e.ActorEmployeeId, e.Summary,
                e.BeforeJson, e.AfterJson, e.EmployeeId, e.EntityId, e.CorrelationId))
            .ToListAsync(cancellationToken);

        return new HR.SharedKernel.PagedResult<AuditHistoryEntry>(
            items, totalCount, pagination.PageNumber, pagination.PageSize);
    }

    public async Task<HR.SharedKernel.PagedResult<AuditHistoryEntry>> GetPlatformAuditLogAsync(
        Guid? companyId,
        IReadOnlyCollection<Guid>? actorUserIds,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        string? eventType,
        HR.SharedKernel.Pagination pagination,
        CancellationToken cancellationToken)
    {
        var query = context.AuditEvents.AsNoTracking().AsQueryable();

        if (companyId.HasValue)
            query = query.Where(e => e.CompanyId == companyId.Value);

        if (actorUserIds is { Count: > 0 })
            query = query.Where(e => e.ActorUserId != null && actorUserIds.Contains(e.ActorUserId.Value));

        if (fromDate.HasValue)
            query = query.Where(e => e.OccurredAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(e => e.OccurredAt <= toDate.Value);

        if (!string.IsNullOrWhiteSpace(eventType))
            query = query.Where(e => e.EventType == eventType);

        query = query.OrderByDescending(e => e.OccurredAt);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip(pagination.Offset)
            .Take(pagination.PageSize)
            .Select(e => new AuditHistoryEntry(
                e.OccurredAt, e.EventType, e.EntityType, e.ActorUserId, e.ActorEmployeeId, e.Summary,
                e.BeforeJson, e.AfterJson, e.EmployeeId, e.EntityId, e.CorrelationId, e.CompanyId))
            .ToListAsync(cancellationToken);

        return new HR.SharedKernel.PagedResult<AuditHistoryEntry>(
            items, totalCount, pagination.PageNumber, pagination.PageSize);
    }
}
