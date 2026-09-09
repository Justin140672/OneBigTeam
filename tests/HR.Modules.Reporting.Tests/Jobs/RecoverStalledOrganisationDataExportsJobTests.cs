using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Jobs;
using HR.Modules.Reporting.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Reporting.Tests.Jobs;

public class RecoverStalledOrganisationDataExportsJobTests
{
    private static readonly DateTime Now = new(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);

    private static RecoverStalledOrganisationDataExportsJob Build(
        FakeRecoveryJobStore store, RecordingBackgroundJobClient client, FakeRecoveryStorage? storage = null) =>
        new(store, client, storage ?? new FakeRecoveryStorage(), new FakeClock(Now),
            NullLogger<RecoverStalledOrganisationDataExportsJob>.Instance);

    private static OrganisationDataExportJobView Pending(Guid companyId, Guid? userId = null) =>
        new(Guid.NewGuid(), companyId, "Pending", null, null, userId ?? Guid.NewGuid());

    private static OrganisationDataExportJobView InProgress(Guid companyId, int attemptCount, Guid? userId = null) =>
        new(Guid.NewGuid(), companyId, "InProgress", null, null, userId ?? Guid.NewGuid(),
            attemptCount, Now.AddMinutes(-90), Now.AddMinutes(-45),
            LeaseOwnerToken: Guid.NewGuid(), LeaseExpiresAt: Now.AddMinutes(-30));

    [Fact]
    public async Task Stale_Pending_Row_Is_Re_Enqueued_As_Build_Job()
    {
        var companyId = Guid.NewGuid();
        var row = Pending(companyId);
        var store = new FakeRecoveryJobStore(row);
        var client = new RecordingBackgroundJobClient();

        await Build(store, client).ExecuteAsync();

        var job = Assert.Single(client.Enqueued);
        Assert.Equal(typeof(OrganisationDataExportBuildJob), job.Type);
        Assert.Equal(nameof(OrganisationDataExportBuildJob.RunAsync), job.Method.Name);
        Assert.Equal(row.Id, job.Args[0]);
        Assert.Equal(companyId, job.Args[1]);
        Assert.Equal(row.RequestedByUserId, job.Args[2]);
        Assert.Empty(store.ResetForRetryCalls);
        Assert.Empty(store.MarkFailedCalls);
    }

    [Fact]
    public async Task Expired_Lease_InProgress_With_Attempts_Left_Is_Reset_Then_Re_Enqueued()
    {
        var companyId = Guid.NewGuid();
        var row = InProgress(companyId, attemptCount: 2);
        var store = new FakeRecoveryJobStore(row);
        var client = new RecordingBackgroundJobClient();

        await Build(store, client).ExecuteAsync();

        Assert.Equal(new[] { row.Id }, store.ResetForRetryCalls);
        var job = Assert.Single(client.Enqueued);
        Assert.Equal(row.Id, job.Args[0]);
        Assert.Equal(companyId, job.Args[1]);
        Assert.Equal(row.RequestedByUserId, job.Args[2]);
        Assert.Empty(store.MarkFailedCalls);
    }

    [Fact]
    public async Task Expired_Lease_InProgress_At_Max_Attempts_Is_Failed_And_Not_Re_Enqueued()
    {
        var companyId = Guid.NewGuid();
        var row = InProgress(companyId, attemptCount: 3);
        var store = new FakeRecoveryJobStore(row);
        var client = new RecordingBackgroundJobClient();

        await Build(store, client).ExecuteAsync();

        var failed = Assert.Single(store.MarkFailedCalls);
        Assert.Equal(row.Id, failed.ExportId);
        Assert.Contains("interrupted", failed.Reason);
        Assert.Contains("repeated attempts", failed.Reason);
        Assert.Empty(client.Enqueued);
        Assert.Empty(store.ResetForRetryCalls);
    }

    [Fact]
    public async Task Requests_Recoverable_Rows_With_Pending_Grace_Cutoff_And_Lease_Expired_As_Of_Now()
    {
        var store = new FakeRecoveryJobStore();
        await Build(store, new RecordingBackgroundJobClient()).ExecuteAsync();

        Assert.Equal(
            new DateTimeOffset(Now).AddMinutes(-RecoverStalledOrganisationDataExportsJob.PendingQueueGraceMinutes),
            store.PendingCutoff);
        Assert.Equal(new DateTimeOffset(Now), store.LeaseExpiredAsOf);
    }

    [Fact]
    public async Task Stale_InProgress_Recovery_Sweeps_Abandoned_Attempt_Archives_Before_Retry()
    {
        var companyId = Guid.NewGuid();
        var row = InProgress(companyId, attemptCount: 1);
        var store = new FakeRecoveryJobStore(row);
        var client = new RecordingBackgroundJobClient();
        var storage = new FakeRecoveryStorage();
        storage.AttemptKeys[row.Id] =
        [
            $"organisation-exports/{companyId}/{row.Id}/a.zip",
            $"organisation-exports/{companyId}/{row.Id}/b.zip",
        ];

        await Build(store, client, storage).ExecuteAsync();

        Assert.Equal(
            storage.AttemptKeys[row.Id].OrderBy(k => k, StringComparer.Ordinal).ToArray(),
            storage.DeletedKeys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { row.Id }, store.ResetForRetryCalls);
        Assert.Single(client.Enqueued);
        Assert.Empty(store.MarkFailedCalls);
    }

    [Fact]
    public async Task Exhausted_InProgress_Recovery_Sweeps_Attempt_Archives_Then_Fails()
    {
        var companyId = Guid.NewGuid();
        var row = InProgress(companyId, attemptCount: 3);
        var store = new FakeRecoveryJobStore(row);
        var client = new RecordingBackgroundJobClient();
        var storage = new FakeRecoveryStorage();
        storage.AttemptKeys[row.Id] = [$"organisation-exports/{companyId}/{row.Id}/a.zip"];

        await Build(store, client, storage).ExecuteAsync();

        Assert.Equal(new[] { $"organisation-exports/{companyId}/{row.Id}/a.zip" }, storage.DeletedKeys);
        Assert.Single(store.MarkFailedCalls);
        Assert.Empty(client.Enqueued);
        Assert.Empty(store.ResetForRetryCalls);
    }

    [Fact]
    public async Task Empty_Batch_Does_Nothing()
    {
        var store = new FakeRecoveryJobStore();
        var client = new RecordingBackgroundJobClient();

        await Build(store, client).ExecuteAsync();

        Assert.Empty(client.Enqueued);
        Assert.Empty(store.ResetForRetryCalls);
        Assert.Empty(store.MarkFailedCalls);
    }

    [Fact]
    public async Task Original_Worker_Completes_Before_Cleanup_Claim_Refused_No_Files_Deleted_No_Reset_Or_Fail()
    {
        var companyId = Guid.NewGuid();
        var row = InProgress(companyId, attemptCount: 1);
        var store = new FakeRecoveryJobStore(row);
        store.ClaimResults[row.Id] = new Queue<bool>([false]);
        var client = new RecordingBackgroundJobClient();
        var storage = new FakeRecoveryStorage();
        storage.AttemptKeys[row.Id] = [$"organisation-exports/{companyId}/{row.Id}/a.zip"];

        await Build(store, client, storage).ExecuteAsync();

        Assert.Single(store.ClaimForRecoveryCalls);
        Assert.Empty(storage.DeletedKeys);
        Assert.Empty(store.ResetForRetryCalls);
        Assert.Empty(store.MarkFailedCalls);
        Assert.Empty(client.Enqueued);
    }

    [Fact]
    public async Task Two_Recovery_Sweeps_Select_The_Same_Export_Only_The_First_Claim_Wins()
    {
        var companyId = Guid.NewGuid();
        var row = InProgress(companyId, attemptCount: 1);
        var store = new FakeRecoveryJobStore(row);
        store.ClaimResults[row.Id] = new Queue<bool>([true, false]);
        var client = new RecordingBackgroundJobClient();

        await Build(store, client).ExecuteAsync(); // wins
        await Build(store, client).ExecuteAsync(); // loses the claim

        Assert.Equal(2, store.ClaimForRecoveryCalls.Count);
        Assert.Equal(new[] { row.Id }, store.ResetForRetryCalls);
        Assert.Single(client.Enqueued);
    }

    [Fact]
    public async Task Reset_Refused_After_Claim_Because_Ownership_Changed_Does_Not_Enqueue()
    {
        var companyId = Guid.NewGuid();
        var row = InProgress(companyId, attemptCount: 1);
        var store = new FakeRecoveryJobStore(row);
        store.RefuseResetFor.Add(row.Id);
        var client = new RecordingBackgroundJobClient();

        await Build(store, client).ExecuteAsync();

        Assert.Single(store.ClaimForRecoveryCalls);
        Assert.Empty(store.ResetForRetryCalls);
        Assert.Empty(client.Enqueued);
    }

    [Fact]
    public async Task Recovery_Cleanup_Never_Deletes_The_Published_Archive_Key()
    {
        var companyId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var publishedKey = $"organisation-exports/{companyId}/{id}/published.zip";
        var row = new OrganisationDataExportJobView(
            id, companyId, "InProgress", publishedKey, null, Guid.NewGuid(),
            1, Now.AddMinutes(-90), Now.AddMinutes(-45),
            LeaseOwnerToken: Guid.NewGuid(), LeaseExpiresAt: Now.AddMinutes(-30));
        var store = new FakeRecoveryJobStore(row);
        var client = new RecordingBackgroundJobClient();
        var storage = new FakeRecoveryStorage();
        storage.AttemptKeys[id] =
        [
            publishedKey,
            $"organisation-exports/{companyId}/{id}/orphan.zip",
        ];

        await Build(store, client, storage).ExecuteAsync();

        Assert.Equal(new[] { $"organisation-exports/{companyId}/{id}/orphan.zip" }, storage.DeletedKeys);
        Assert.DoesNotContain(publishedKey, storage.DeletedKeys);
    }

    [Fact]
    public async Task Abandoned_Attempt_At_Max_Attempts_Is_Swept_Then_Failed_With_Recovery_Token()
    {
        var companyId = Guid.NewGuid();
        var row = InProgress(companyId, attemptCount: 3);
        var store = new FakeRecoveryJobStore(row);
        var client = new RecordingBackgroundJobClient();

        await Build(store, client).ExecuteAsync();

        var failed = Assert.Single(store.MarkFailedCalls);
        Assert.Equal(row.Id, failed.ExportId);
        Assert.Equal(store.ClaimForRecoveryCalls[0].RecoveryToken, failed.RecoveryToken);
        Assert.Empty(client.Enqueued);
    }

    private sealed class FakeRecoveryJobStore(params OrganisationDataExportJobView[] recoverable)
        : IOrganisationDataExportJobStore
    {
        public List<Guid> ResetForRetryCalls { get; } = [];
        public List<(Guid ExportId, Guid RecoveryToken, string Reason)> MarkFailedCalls { get; } = [];
        public List<(Guid ExportId, Guid RecoveryToken)> ClaimForRecoveryCalls { get; } = [];
        public DateTimeOffset PendingCutoff { get; private set; }
        public DateTimeOffset LeaseExpiredAsOf { get; private set; }

        /// <summary>Follow-up H: per-export claim outcome. Missing entry defaults to <c>true</c>.</summary>
        public Dictionary<Guid, Queue<bool>> ClaimResults { get; } = [];

        /// <summary>Follow-up H: per-export reset outcome. Missing entry defaults to <c>true</c>.</summary>
        public HashSet<Guid> RefuseResetFor { get; } = [];

        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetRecoverableAsync(
            DateTimeOffset pendingQueuedBefore, DateTimeOffset leaseExpiredAsOf, CancellationToken cancellationToken)
        {
            PendingCutoff = pendingQueuedBefore;
            LeaseExpiredAsOf = leaseExpiredAsOf;
            return Task.FromResult<IReadOnlyList<OrganisationDataExportJobView>>(recoverable);
        }

        public Task<bool> ClaimForRecoveryAsync(Guid exportId, Guid recoveryToken, CancellationToken cancellationToken)
        {
            ClaimForRecoveryCalls.Add((exportId, recoveryToken));
            var result = ClaimResults.TryGetValue(exportId, out var q) && q.Count > 0 ? q.Dequeue() : true;
            return Task.FromResult(result);
        }

        public Task<bool> ResetForRetryAsync(Guid exportId, Guid recoveryToken, CancellationToken cancellationToken)
        {
            if (RefuseResetFor.Contains(exportId))
                return Task.FromResult(false);
            ResetForRetryCalls.Add(exportId);
            return Task.FromResult(true);
        }

        public Task<bool> MarkFailedAsync(Guid exportId, Guid ownerToken, string failureReason, CancellationToken cancellationToken)
        {
            MarkFailedCalls.Add((exportId, ownerToken, failureReason));
            return Task.FromResult(true);
        }

        public Task ResetForRetryAsync(Guid exportId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task MarkFailedAsync(Guid exportId, string failureReason, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<OrganisationDataExportJobView?> GetAsync(Guid exportId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task MarkInProgressAsync(Guid exportId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> BeginAttemptAsync(Guid exportId, Guid ownerToken, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> RenewLeaseAsync(Guid exportId, Guid ownerToken, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> MarkCompletedAsync(Guid exportId, Guid ownerToken, string storageKey, long fileSizeBytes, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> MarkFailedDueToMissingDocumentsAsync(Guid exportId, Guid ownerToken, int missingCount, CancellationToken cancellationToken) =>
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
        public Task<IReadOnlyList<OrganisationDataExportJobView>> GetExpiredAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task MarkExpiredAsync(Guid exportId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeRecoveryStorage : IOrganisationDataExportStorage
    {
        public Dictionary<Guid, List<string>> AttemptKeys { get; } = [];
        public List<string> DeletedKeys { get; } = [];

        public Task<IReadOnlyList<string>> ListAttemptKeysAsync(Guid companyId, Guid exportId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(AttemptKeys.TryGetValue(exportId, out var keys) ? keys : []);

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
        {
            DeletedKeys.Add(storageKey);
            return Task.CompletedTask;
        }

        public Task<string> UploadAsync(Guid companyId, Guid exportId, Guid attemptToken, Stream content, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingBackgroundJobClient : IBackgroundJobClient
    {
        public List<Job> Enqueued { get; } = [];

        public string Create(Job job, IState state)
        {
            Enqueued.Add(job);
            return Guid.NewGuid().ToString();
        }

        public bool ChangeState(string jobId, IState state, string? expectedState) => true;
    }
}
