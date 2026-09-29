using HR.Infrastructure.Abstractions;

namespace HR.Modules.Companies.Tests.Infrastructure;

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
