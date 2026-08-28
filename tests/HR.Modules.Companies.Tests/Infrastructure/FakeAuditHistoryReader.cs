using HR.SharedKernel;

namespace HR.Modules.Companies.Tests.Infrastructure;

/// <summary>
/// Test double for <see cref="IAuditHistoryReader"/> — lets GetAuditLogHandler tests supply a fixed
/// set of platform-wide audit entries and apply the same filter/paging semantics as the real
/// AuditHistoryReader.GetPlatformAuditLogAsync, without a real AuditDbContext/database. The other
/// three overloads on the interface aren't exercised by GetAuditLog and simply return an empty list.
/// </summary>
internal sealed class FakeAuditHistoryReader : IAuditHistoryReader
{
    public IReadOnlyList<AuditHistoryEntry> PlatformEntries { get; set; } = [];

    public Task<IReadOnlyList<AuditHistoryEntry>> GetEmployeeAuditHistoryAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AuditHistoryEntry>>([]);

    public Task<IReadOnlyList<AuditHistoryEntry>> GetRecentCompanyAuditHistoryAsync(
        Guid companyId, IReadOnlyCollection<string> entityTypes, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AuditHistoryEntry>>([]);

    public Task<IReadOnlyList<AuditHistoryEntry>> GetEntityAuditHistoryAsync(
        Guid companyId, string entityType, Guid entityId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AuditHistoryEntry>>([]);

    public Task<PagedResult<AuditHistoryEntry>> GetPlatformAuditLogAsync(
        Guid? companyId,
        IReadOnlyCollection<Guid>? actorUserIds,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        string? eventType,
        Pagination pagination,
        CancellationToken cancellationToken)
    {
        var query = PlatformEntries.AsEnumerable();

        if (companyId.HasValue)
            query = query.Where(e => e.CompanyId == companyId.Value);

        if (actorUserIds is { Count: > 0 })
            query = query.Where(e => e.ActorUserId.HasValue && actorUserIds.Contains(e.ActorUserId.Value));

        if (fromDate.HasValue)
            query = query.Where(e => e.OccurredAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(e => e.OccurredAt <= toDate.Value);

        if (!string.IsNullOrWhiteSpace(eventType))
            query = query.Where(e => e.EventType == eventType);

        query = query.OrderByDescending(e => e.OccurredAt);

        var all = query.ToList();
        var totalCount = all.Count;

        var items = all
            .Skip(pagination.Offset)
            .Take(pagination.PageSize)
            .ToList();

        return Task.FromResult(new PagedResult<AuditHistoryEntry>(
            items, totalCount, pagination.PageNumber, pagination.PageSize));
    }

    public Task<PagedResult<AuditHistoryEntry>> GetCompanyAuditLogAsync(
        Guid companyId,
        Guid? employeeId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        string? eventType,
        Pagination pagination,
        CancellationToken cancellationToken)
    {
        var query = PlatformEntries.Where(e => e.CompanyId == companyId);
        if (employeeId.HasValue) query = query.Where(e => e.EmployeeId == employeeId);
        if (fromDate.HasValue)   query = query.Where(e => e.OccurredAt >= fromDate.Value);
        if (toDate.HasValue)     query = query.Where(e => e.OccurredAt <= toDate.Value);
        if (!string.IsNullOrWhiteSpace(eventType)) query = query.Where(e => e.EventType == eventType);
        var all = query.OrderByDescending(e => e.OccurredAt).ToList();
        var items = all.Skip(pagination.Offset).Take(pagination.PageSize).ToList();
        return Task.FromResult(new PagedResult<AuditHistoryEntry>(items, all.Count, pagination.PageNumber, pagination.PageSize));
    }
}
