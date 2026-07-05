using HR.SharedKernel;

namespace HR.Modules.Employees.Tests.Infrastructure;

internal sealed class FakeAuditHistoryReader(IReadOnlyList<AuditHistoryEntry> entries) : IAuditHistoryReader
{
    public Task<IReadOnlyList<AuditHistoryEntry>> GetEmployeeAuditHistoryAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken) =>
        Task.FromResult(entries);
}
