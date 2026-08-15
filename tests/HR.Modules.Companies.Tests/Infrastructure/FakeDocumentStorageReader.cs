using HR.Infrastructure.Abstractions;

namespace HR.Modules.Companies.Tests.Infrastructure;

/// <summary>
/// Minimal test double for <see cref="IDocumentStorageReader"/> — returns a pre-configured
/// storage-usage summary so <c>GetCustomerDetailsHandler</c> tests can assert
/// <c>TotalStorageBytes</c>/<c>StorageFileCount</c> without a real Documents-schema query.
/// </summary>
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
