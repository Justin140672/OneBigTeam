namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Provides an aggregate storage-usage summary for a company, as owned by HR.Modules.Documents.
/// Used by HR.Modules.Companies (platform admin Customer Details) without a direct module-to-module
/// reference or database join.
/// </summary>
public interface IDocumentStorageReader
{
    Task<DocumentStorageUsage> GetStorageUsageAsync(Guid companyId, CancellationToken cancellationToken);
}

public sealed record DocumentStorageUsage(
    long TotalStorageBytes,
    int FileCount);
