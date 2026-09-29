namespace HR.Infrastructure.Abstractions;

public interface IDocumentStorageReader
{
    Task<DocumentStorageUsage> GetStorageUsageAsync(Guid companyId, CancellationToken cancellationToken);
}

public sealed record DocumentStorageUsage(
    long TotalStorageBytes,
    int FileCount);
