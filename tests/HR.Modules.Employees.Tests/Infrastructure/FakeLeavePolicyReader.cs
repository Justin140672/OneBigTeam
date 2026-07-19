using HR.Infrastructure.Abstractions;

namespace HR.Modules.Employees.Tests.Infrastructure;

internal sealed class FakeLeavePolicyReader(bool exists = true, Guid? defaultLeavePolicyId = null) : ILeavePolicyReader
{
    public Task<bool> ExistsAsync(Guid companyId, Guid leavePolicyId, CancellationToken cancellationToken)
        => Task.FromResult(exists);

    public Task<Guid?> GetDefaultLeavePolicyIdAsync(Guid companyId, CancellationToken cancellationToken)
        => Task.FromResult(defaultLeavePolicyId);
}
