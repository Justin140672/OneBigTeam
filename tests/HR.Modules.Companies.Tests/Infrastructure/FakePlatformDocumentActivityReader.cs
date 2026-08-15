using HR.Infrastructure.Abstractions;

namespace HR.Modules.Companies.Tests.Infrastructure;

/// <summary>
/// Minimal test double for <see cref="IPlatformDocumentActivityReader"/> — returns a
/// pre-configured platform-wide document activity summary so
/// GetApplicationMetricsHandler tests can assert the mapped fields without a real
/// HR.Modules.Documents dependency.
/// </summary>
internal sealed class FakePlatformDocumentActivityReader : IPlatformDocumentActivityReader
{
    public PlatformDocumentActivity ActivityToReturn { get; set; } =
        new(TotalStorageBytes: 0, DailyUploads: []);

    public DateOnly? LastFromDate { get; private set; }
    public DateOnly? LastToDate { get; private set; }

    public Task<PlatformDocumentActivity> GetPlatformActivityAsync(
        DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
    {
        LastFromDate = fromDate;
        LastToDate = toDate;
        return Task.FromResult(ActivityToReturn);
    }
}
