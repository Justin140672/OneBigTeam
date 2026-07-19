namespace HR.SharedKernel;

public interface IAuditHistoryReader
{
    Task<IReadOnlyList<AuditHistoryEntry>> GetEmployeeAuditHistoryAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken);

    /// <summary>
    /// Company-wide (not per-employee) recent audit entries, restricted to the given
    /// audit EntityType values and capped at <paramref name="take"/> most recent, newest first.
    /// Used for dashboard "recent changes" widgets — the per-employee overload above can't
    /// answer "what changed across the company recently".
    /// </summary>
    Task<IReadOnlyList<AuditHistoryEntry>> GetRecentCompanyAuditHistoryAsync(
        Guid companyId, IReadOnlyCollection<string> entityTypes, int take, CancellationToken cancellationToken);

    /// <summary>
    /// Full audit history for one specific entity (e.g. a single SharedCompanyDocument), newest
    /// first. Unlike the per-employee overload above, this is not scoped to an employee — it
    /// answers "what happened to this record", regardless of who it happened to.
    /// </summary>
    Task<IReadOnlyList<AuditHistoryEntry>> GetEntityAuditHistoryAsync(
        Guid companyId, string entityType, Guid entityId, CancellationToken cancellationToken);
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
    Guid EntityId = default);
