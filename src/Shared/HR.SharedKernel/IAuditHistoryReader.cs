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

    /// <summary>
    /// Platform-wide (cross-company) paged audit log for the Admin Portal's Platform Audit Log
    /// story. Unlike every overload above, this is not scoped to a single company — companyId here
    /// is an optional filter, not a mandatory tenant boundary, because the caller is always a
    /// platform administrator (gated by the "platform:admin" policy + PlatformAdmin:AllowedEmails
    /// allow-list at the handler level, same as every other Admin Portal platform-wide read).
    /// actorUserIds, when supplied, restricts results to audit rows whose ActorUserId is in the
    /// set (used to implement "filter by administrator", since email isn't stored on the audit row
    /// itself — see IUserEmailDirectoryReader).
    /// </summary>
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
    // Used to merge multiple audit rows produced by the same user action into a single
    // AuditHistoryItem (e.g. EmployeeEdit.razor's combined Profile + Employment tab save) — see
    // GetEmployeeAuditHistoryHandler. Null (the default) means the entry is never merged with
    // anything else, which is the correct behaviour for every existing audit event that doesn't
    // set a CorrelationId.
    Guid? CorrelationId = null,
    // Populated by GetPlatformAuditLogAsync (the only caller that needs it — the per-employee/
    // per-entity overloads above already know the company from their own input parameter). Default
    // (Guid.Empty) for every other overload/existing caller, matching the "trailing optional
    // parameter added without disturbing existing positional callers" convention already used for
    // EmployeeId/EntityId/CorrelationId above.
    Guid CompanyId = default);
