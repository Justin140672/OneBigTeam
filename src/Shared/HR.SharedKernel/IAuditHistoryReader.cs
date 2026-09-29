namespace HR.SharedKernel;

public interface IAuditHistoryReader
{
    Task<IReadOnlyList<AuditHistoryEntry>> GetEmployeeAuditHistoryAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AuditHistoryEntry>> GetRecentCompanyAuditHistoryAsync(
        Guid companyId, IReadOnlyCollection<string> entityTypes, int take, CancellationToken cancellationToken);

    Task<IReadOnlyList<AuditHistoryEntry>> GetEntityAuditHistoryAsync(
        Guid companyId, string entityType, Guid entityId, CancellationToken cancellationToken);

    Task<PagedResult<AuditHistoryEntry>> GetCompanyAuditLogAsync(
        Guid companyId,
        Guid? employeeId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        string? eventType,
        Pagination pagination,
        CancellationToken cancellationToken);

    Task<PagedResult<AuditHistoryEntry>> GetPlatformAuditLogAsync(
        Guid? companyId,
        IReadOnlyCollection<Guid>? actorUserIds,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        string? eventType,
        Pagination pagination,
        CancellationToken cancellationToken);
}

public sealed record AuditHistoryEntry(
    DateTimeOffset OccurredAt,
    string EventType,
    string EntityType,
    Guid? ActorUserId,
    Guid? ActorEmployeeId,
    string? Summary,
    string? BeforeJson,
    string? AfterJson,
    Guid? EmployeeId = null,
    Guid EntityId = default,
    Guid? CorrelationId = null,
    Guid CompanyId = default);
