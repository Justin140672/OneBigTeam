using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Onboarding.Tests.Infrastructure;

internal sealed class FakeManagerReader(Guid? managerId = null) : IManagerReader
{
    public Task<Guid?> GetManagerIdAsync(
        Guid companyId,
        Guid employeeId,
        CancellationToken cancellationToken) =>
        Task.FromResult(managerId);
}
