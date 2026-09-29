using HR.Infrastructure.Abstractions;

namespace HR.Modules.Companies.Tests.Infrastructure;

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
