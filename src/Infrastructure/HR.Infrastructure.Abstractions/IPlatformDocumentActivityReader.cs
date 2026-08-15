namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Provides a platform-wide (not company-scoped) aggregate of document activity across every
/// document-bearing table, for the Admin Portal Application Metrics dashboard (Platform Monitoring
/// epic). Implemented in HR.Modules.Documents, consumed by HR.Modules.Companies without a direct
/// module-to-module reference — same cross-module pattern as IDocumentStorageReader.
/// </summary>
public interface IPlatformDocumentActivityReader
{
    Task<PlatformDocumentActivity> GetPlatformActivityAsync(
        DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken);
}

public sealed record PlatformDocumentActivity(
    long TotalStorageBytes,
    IReadOnlyList<DailyUploadCount> DailyUploads);

public sealed record DailyUploadCount(DateOnly Date, int Count);
