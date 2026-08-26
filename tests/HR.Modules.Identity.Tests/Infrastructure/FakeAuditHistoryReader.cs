using HR.SharedKernel;

namespace HR.Modules.Identity.Tests.Infrastructure;

/// <summary>
/// Minimal test double for <see cref="IAuditHistoryReader"/> — only
/// <see cref="GetEmployeeAuditHistoryAsync"/> is used by GetUserAuditHistoryHandler, so every other
/// member is left unimplemented.
/// </summary>
internal sealed class FakeAuditHistoryReader(IReadOnlyList<AuditHistoryEntry>? entries = null) : IAuditHistoryReader
{
    private readonly IReadOnlyList<AuditHistoryEntry> _entries = entries ?? [];

    public bool WasCalled { get; private set; }

    public Task<IReadOnlyList<AuditHistoryEntry>> GetEmployeeAuditHistoryAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken)
    {
        WasCalled = true;
        return Task.FromResult(_entries);
    }

    public Task<IReadOnlyList<AuditHistoryEntry>> GetRecentCompanyAuditHistoryAsync(
        Guid companyId, IReadOnlyCollection<string> entityTypes, int take, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<IReadOnlyList<AuditHistoryEntry>> GetEntityAuditHistoryAsync(
        Guid companyId, string entityType, Guid entityId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<PagedResult<AuditHistoryEntry>> GetPlatformAuditLogAsync(
        Guid? companyId,
        IReadOnlyCollection<Guid>? actorUserIds,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        string? eventType,
        Pagination pagination,
        CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
