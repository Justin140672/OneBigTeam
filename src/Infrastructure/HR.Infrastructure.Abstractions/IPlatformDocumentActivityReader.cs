namespace HR.Infrastructure.Abstractions;

public interface IPlatformDocumentActivityReader
{
    Task<PlatformDocumentActivity> GetPlatformActivityAsync(
        DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken);
}

public sealed record PlatformDocumentActivity(
    long TotalStorageBytes,
    IReadOnlyList<DailyUploadCount> DailyUploads);

public sealed record DailyUploadCount(DateOnly Date, int Count);
