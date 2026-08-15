using HR.Infrastructure.Abstractions;

namespace HR.Modules.Companies.Tests.Infrastructure;

/// <summary>
/// Minimal test double for <see cref="ICompanyUserCountReader"/> — returns a pre-configured
/// count so GetCustomerSupportViewHandler tests can assert UserCount without a real
/// identity.user_profiles query.
/// </summary>
internal sealed class FakeCompanyUserCountReader : ICompanyUserCountReader
{
    public int CountToReturn { get; set; }

    public Guid? LastCompanyId { get; private set; }

    public Task<int> GetUserCountAsync(Guid companyId, CancellationToken cancellationToken)
    {
        LastCompanyId = companyId;
        return Task.FromResult(CountToReturn);
    }
}
