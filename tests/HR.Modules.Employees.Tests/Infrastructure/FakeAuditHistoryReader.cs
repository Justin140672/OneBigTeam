using HR.SharedKernel;

namespace HR.Modules.Employees.Tests.Infrastructure;

internal sealed class FakeAuditHistoryReader(IReadOnlyList<AuditHistoryEntry> entries) : IAuditHistoryReader
{
    public Task<IReadOnlyList<AuditHistoryEntry>> GetEmployeeAuditHistoryAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken) =>
        Task.FromResult(entries);

    public Task<IReadOnlyList<AuditHistoryEntry>> GetRecentCompanyAuditHistoryAsync(
        Guid companyId, IReadOnlyCollection<string> entityTypes, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AuditHistoryEntry>>(
            entries.Where(e => entityTypes.Contains(e.EntityType)).Take(take).ToList());

    public Task<IReadOnlyList<AuditHistoryEntry>> GetEntityAuditHistoryAsync(
        Guid companyId, string entityType, Guid entityId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AuditHistoryEntry>>(
            entries.Where(e => e.EntityType == entityType && e.EntityId == entityId).ToList());

    public Task<PagedResult<AuditHistoryEntry>> GetPlatformAuditLogAsync(
        Guid? companyId,
        IReadOnlyCollection<Guid>? actorUserIds,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        string? eventType,
        Pagination pagination,
        CancellationToken cancellationToken) =>
        Task.FromResult(new PagedResult<AuditHistoryEntry>([], 0, pagination.PageNumber, pagination.PageSize));

    public Task<PagedResult<AuditHistoryEntry>> GetCompanyAuditLogAsync(
        Guid companyId, Guid? employeeId, DateTimeOffset? fromDate, DateTimeOffset? toDate,
        string? eventType, Pagination pagination, CancellationToken cancellationToken) =>
        Task.FromResult(new PagedResult<AuditHistoryEntry>([], 0, pagination.PageNumber, pagination.PageSize));
}
