using HR.Infrastructure.Abstractions;

namespace HR.Modules.Companies.Tests.Infrastructure;

internal sealed class FakeDocumentStorageReader : IDocumentStorageReader
{
    public DocumentStorageUsage UsageToReturn { get; set; } = new(TotalStorageBytes: 0, FileCount: 0);

    public Guid? LastCompanyId { get; private set; }

    public Task<DocumentStorageUsage> GetStorageUsageAsync(Guid companyId, CancellationToken cancellationToken)
    {
        LastCompanyId = companyId;
        return Task.FromResult(UsageToReturn);
    }
}
