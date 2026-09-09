using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Jobs;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Reporting.Tests;

public class PurgeExpiredOrganisationDataExportsJobTests
{
    private static OrganisationDataExportJobView View(Guid companyId, string? storageKey) =>
        new(Guid.NewGuid(), companyId, "Completed", storageKey, DateTimeOffset.UtcNow.AddDays(-1));

    private static PurgeExpiredOrganisationDataExportsJob Build(
        FakeJobStore store, FakeStorage storage, FakeLegalHoldReader legalHold)
    {
        var log = new List<string>();
        store.Log = log;
        storage.Log = log;
        return new(store, storage, legalHold, NullLogger<PurgeExpiredOrganisationDataExportsJob>.Instance);
    }

    [Fact]
    public async Task Company_under_legal_hold_is_skipped()
    {
        var companyId = Guid.NewGuid();
        var export = View(companyId, "organisation-exports/a/b.zip");
        var store = new FakeJobStore(export);
        var storage = new FakeStorage();
        var legalHold = new FakeLegalHoldReader { HeldCompanies = { companyId } };

        await Build(store, storage, legalHold).ExecuteAsync();

        Assert.Empty(storage.DeletedKeys);
        Assert.Empty(store.MarkedExpired);
    }

    [Fact]
    public async Task Happy_path_deletes_then_marks_expired()
    {
        var export = View(Guid.NewGuid(), "organisation-exports/a/b.zip");
        var store = new FakeJobStore(export);
        var storage = new FakeStorage();
        var job = Build(store, storage, new FakeLegalHoldReader());

        await job.ExecuteAsync();

        Assert.Equal(new[] { "organisation-exports/a/b.zip" }, storage.DeletedKeys);
        Assert.Equal(new[] { export.Id }, store.MarkedExpired);
        Assert.Equal(new[] { $"delete:{export.StorageKey}", $"markExpired:{export.Id}" }, store.Log);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_storage_key_marks_expired_without_delete(string? storageKey)
    {
        var export = View(Guid.NewGuid(), storageKey);
        var store = new FakeJobStore(export);
        var storage = new FakeStorage();

        await Build(store, storage, new FakeLegalHoldReader()).ExecuteAsync();

        Assert.Empty(storage.DeletedKeys);
        Assert.Equal(new[] { export.Id }, store.MarkedExpired);
    }

    [Fact]
    public async Task Delete_failure_is_swallowed_and_export_still_marked_expired()
    {
        var export = View(Guid.NewGuid(), "organisation-exports/a/b.zip");
        var store = new FakeJobStore(export);
        var storage = new FakeStorage { ThrowForKeys = { "organisation-exports/a/b.zip" } };

        await Build(store, storage, new FakeLegalHoldReader()).ExecuteAsync();

        Assert.Equal(new[] { export.Id }, store.MarkedExpired);
    }

    [Fact]
    public async Task One_delete_failure_does_not_stop_later_exports()
    {
        var first = View(Guid.NewGuid(), "organisation-exports/1/a.zip");
        var second = View(Guid.NewGuid(), "organisation-exports/2/b.zip");
        var store = new FakeJobStore(first, second);
        var storage = new FakeStorage { ThrowForKeys = { "organisation-exports/1/a.zip" } };

        await Build(store, storage, new FakeLegalHoldReader()).ExecuteAsync();

        Assert.Equal(new[] { first.Id, second.Id }, store.MarkedExpired);
        Assert.Contains("organisation-exports/2/b.zip", storage.DeletedKeys);
    }

    [Fact]
    public async Task Purge_Deletes_Every_Attempt_Archive_Not_Just_The_Published_One()
    {
        var export = View(Guid.NewGuid(), "organisation-exports/a/e/published.zip");
        var store = new FakeJobStore(export);
        var storage = new FakeStorage();
        storage.AttemptKeys[export.Id] =
        [
            "organisation-exports/a/e/published.zip",
            "organisation-exports/a/e/orphan-1.zip",
            "organisation-exports/a/e/orphan-2.zip",
        ];

        await Build(store, storage, new FakeLegalHoldReader()).ExecuteAsync();

        Assert.Equal(
            new[]
            {
                "organisation-exports/a/e/orphan-1.zip",
                "organisation-exports/a/e/orphan-2.zip",
                "organisation-exports/a/e/published.zip",
            },
            storage.DeletedKeys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { export.Id }, store.MarkedExpired);
        Assert.Equal($"markExpired:{export.Id}", store.Log[^1]);
    }

    [Fact]
    public async Task Empty_batch_does_nothing()
    {
        var store = new FakeJobStore();
        var storage = new FakeStorage();

        await Build(store, storage, new FakeLegalHoldReader()).ExecuteAsync();

        Assert.Empty(storage.DeletedKeys);
        Assert.Empty(store.MarkedExpired);
    }

    private sealed class FakeJobStore(params OrganisationDataExportJobView[] expired) : IOrganisationDataExportJobStore
    {
        public List<Guid> MarkedExpired { get; } = [];
        public List<string> Log { get; set; } = [];

        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetExpiredAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OrganisationDataExportJobView>>(expired);

        public Task MarkExpiredAsync(Guid exportId, CancellationToken cancellationToken)
        {
            Log.Add($"markExpired:{exportId}");
            MarkedExpired.Add(exportId);
            return Task.CompletedTask;
        }

        public Task<OrganisationDataExportJobView?> GetAsync(Guid exportId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task MarkInProgressAsync(Guid exportId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> MarkCompletedAsync(Guid exportId, Guid ownerToken, string storageKey, long fileSizeBytes, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task MarkFailedAsync(Guid exportId, string failureReason, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> MarkFailedAsync(Guid exportId, Guid ownerToken, string failureReason, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> BeginAttemptAsync(Guid exportId, Guid ownerToken, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> RenewLeaseAsync(Guid exportId, Guid ownerToken, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> MarkFailedDueToMissingDocumentsAsync(Guid exportId, Guid ownerToken, int missingCount, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task ResetForRetryAsync(Guid exportId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> ClaimForRecoveryAsync(Guid exportId, Guid recoveryToken, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> ResetForRetryAsync(Guid exportId, Guid recoveryToken, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetArtefactCleanupCandidatesAsync(int batchSize, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetRecentlyCleanedArtefactsAsync(int batchSize, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task MarkAttemptFilesCleanedAsync(Guid exportId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task DeferArtefactCleanupAsync(Guid exportId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task RecordLateUploadRecheckAsync(Guid exportId, bool succeeded, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetRecoverableAsync(
            DateTimeOffset pendingQueuedBefore, DateTimeOffset leaseExpiredAsOf, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeStorage : IOrganisationDataExportStorage
    {
        public List<string> DeletedKeys { get; } = [];
        public HashSet<string> ThrowForKeys { get; } = [];
        public List<string> Log { get; set; } = [];

        /// <summary>Follow-up D: attempt keys returned by <see cref="ListAttemptKeysAsync"/>, keyed by exportId.</summary>
        public Dictionary<Guid, List<string>> AttemptKeys { get; } = [];

        public Task<IReadOnlyList<string>> ListAttemptKeysAsync(Guid companyId, Guid exportId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(
                AttemptKeys.TryGetValue(exportId, out var keys) ? keys : []);

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
        {
            Log.Add($"delete:{storageKey}");
            if (ThrowForKeys.Contains(storageKey))
            {
                throw new InvalidOperationException("storage boom");
            }

            DeletedKeys.Add(storageKey);
            return Task.CompletedTask;
        }

        public Task<string> UploadAsync(Guid companyId, Guid exportId, Guid attemptToken, Stream content, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeLegalHoldReader : ILegalHoldStatusReader
    {
        public HashSet<Guid> HeldCompanies { get; } = [];

        public Task<bool> IsUnderLegalHoldAsync(Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult(HeldCompanies.Contains(companyId));
    }
}
