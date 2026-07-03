using HR.SharedKernel.Contracts;

namespace HR.Modules.Sickness.Tests.Infrastructure;

internal sealed class FakeManagerReader(Guid? managerId = null) : IManagerReader
{
    public Task<Guid?> GetManagerIdAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken) =>
        Task.FromResult(managerId);
}
