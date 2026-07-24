using HR.Infrastructure.Abstractions;

namespace HR.Modules.Documents.Tests.Infrastructure;

internal sealed class FakeManagerReader(Guid? managerId = null) : IManagerReader
{
    public Task<Guid?> GetManagerIdAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken) =>
        Task.FromResult(managerId);
}
