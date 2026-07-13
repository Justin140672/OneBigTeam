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
}
