using HR.Modules.Recruitment.Jobs;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// Ticket 7 (P2): PurgeCandidateDocumentStorageJob is the Hangfire-retried job that deletes a purged
/// candidate document's blob from storage after PurgeEligibleCandidatesHandler has already hard-deleted
/// the owning CandidateDocument row. See PurgeEligibleCandidatesHandlerTests for the coverage of that
/// enqueueing behaviour.
/// </summary>
public class PurgeCandidateDocumentStorageJobTests
{
    [Fact]
    public async Task ProcessAsync_Calls_Storage_DeleteAsync_With_Given_StorageKey()
    {
        var storage = new FakeCandidateDocumentStorageService();
        var job = new PurgeCandidateDocumentStorageJob(storage, NullLogger<PurgeCandidateDocumentStorageJob>.Instance);
        var storageKey = "company/candidate/doc/cv.pdf";

        await job.ProcessAsync(storageKey);

        var deleted = Assert.Single(storage.Deletions);
        Assert.Equal(storageKey, deleted);
    }

    [Fact]
    public async Task ProcessAsync_Rethrows_When_Storage_DeleteAsync_Throws_So_Hangfire_Retries()
    {
        var storage = new ThrowingCandidateDocumentStorageService();
        var job = new PurgeCandidateDocumentStorageJob(storage, NullLogger<PurgeCandidateDocumentStorageJob>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ProcessAsync("some-storage-key"));
    }

    private sealed class ThrowingCandidateDocumentStorageService : ICandidateDocumentStorageService
    {
        public Task<string> UploadAsync(Stream content, string fileName, string contentType, string storageFolder, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Uri> GetDownloadUrlAsync(string storageKey, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Simulated storage outage.");
    }
}
