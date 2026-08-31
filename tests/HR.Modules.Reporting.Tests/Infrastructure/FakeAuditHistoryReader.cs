using HR.SharedKernel;

namespace HR.Modules.Reporting.Tests.Infrastructure;

/// <summary>
/// ADM-08 test double for <see cref="IAuditHistoryReader"/>. Only <see cref="GetCompanyAuditLogAsync"/>
/// has real behaviour — it returns a configurable <see cref="PagedResult{T}"/> and records the
/// arguments it was called with so governance report handler tests can assert filter pass-through.
/// Every other member throws, since no governance report uses them.
/// </summary>
internal sealed class FakeAuditHistoryReader : IAuditHistoryReader
{
    private readonly IReadOnlyList<AuditHistoryEntry> _entries;
    private readonly int _totalCount;

    public FakeAuditHistoryReader(IReadOnlyList<AuditHistoryEntry>? entries = null, int? totalCount = null)
    {
        _entries = entries ?? [];
        _totalCount = totalCount ?? _entries.Count;
    }

    public Guid LastCompanyId { get; private set; }
    public Guid? LastEmployeeId { get; private set; }
    public DateTimeOffset? LastFromDate { get; private set; }
    public DateTimeOffset? LastToDate { get; private set; }
    public string? LastEventType { get; private set; }
    public Pagination? LastPagination { get; private set; }

    public Task<PagedResult<AuditHistoryEntry>> GetCompanyAuditLogAsync(
        Guid companyId, Guid? employeeId, DateTimeOffset? fromDate, DateTimeOffset? toDate,
        string? eventType, Pagination pagination, CancellationToken cancellationToken)
    {
        LastCompanyId = companyId;
        LastEmployeeId = employeeId;
        LastFromDate = fromDate;
        LastToDate = toDate;
        LastEventType = eventType;
        LastPagination = pagination;

        return Task.FromResult(new PagedResult<AuditHistoryEntry>(
            _entries, _totalCount, pagination.PageNumber, pagination.PageSize));
    }

    public Task<IReadOnlyList<AuditHistoryEntry>> GetEmployeeAuditHistoryAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<AuditHistoryEntry>> GetRecentCompanyAuditHistoryAsync(
        Guid companyId, IReadOnlyCollection<string> entityTypes, int take, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<AuditHistoryEntry>> GetEntityAuditHistoryAsync(
        Guid companyId, string entityType, Guid entityId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<PagedResult<AuditHistoryEntry>> GetPlatformAuditLogAsync(
        Guid? companyId, IReadOnlyCollection<Guid>? actorUserIds, DateTimeOffset? fromDate,
        DateTimeOffset? toDate, string? eventType, Pagination pagination, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
