namespace HR.Infrastructure.Abstractions;

public interface IOrganisationDataExportJobStore
{
    Task<OrganisationDataExportJobView?> GetAsync(Guid exportId, CancellationToken cancellationToken);

    Task MarkInProgressAsync(Guid exportId, CancellationToken cancellationToken);

    /// <summary>
    /// Ticket 3 / Follow-up A: atomically claim this export for a build-job attempt and take out an
    /// ownership lease held by <paramref name="ownerToken"/>. Allowed from Pending, or from InProgress
    /// only when the previous worker's lease has expired. Returns <c>false</c> when the row is missing,
    /// terminal, still owned by a live lease, has exhausted its attempts, or the optimistic-concurrency
    /// guard lost a race — the caller must then abandon the attempt without failing the export.
    /// </summary>
    Task<bool> BeginAttemptAsync(Guid exportId, Guid ownerToken, CancellationToken cancellationToken);

    Task<bool> RenewLeaseAsync(Guid exportId, Guid ownerToken, CancellationToken cancellationToken);

    Task<bool> MarkCompletedAsync(Guid exportId, Guid ownerToken, string storageKey, long fileSizeBytes, CancellationToken cancellationToken);

    Task MarkFailedAsync(Guid exportId, string failureReason, CancellationToken cancellationToken);

    Task<bool> MarkFailedAsync(Guid exportId, Guid ownerToken, string failureReason, CancellationToken cancellationToken);

    /// <summary>
    /// Ticket 3 / Ticket 3J: fail the export because expected documents were genuinely missing from
    /// storage. Loads a fresh row, guards on lease ownership and persists under the optimistic-
    /// concurrency token. Returns <c>false</c> when <paramref name="ownerToken"/> no longer owns the
    /// lease (a replacement worker or recovery sweep has taken over) or the row has moved on — the
    /// caller must then <b>not</b> raise the missing-documents administrative alert.
    /// </summary>
    Task<bool> MarkFailedDueToMissingDocumentsAsync(Guid exportId, Guid ownerToken, int missingCount, CancellationToken cancellationToken);

    /// <summary>Ticket 3: return a stalled InProgress export to Pending so it can be re-enqueued.</summary>
    Task ResetForRetryAsync(Guid exportId, CancellationToken cancellationToken);

    Task<bool> ClaimForRecoveryAsync(Guid exportId, Guid recoveryToken, CancellationToken cancellationToken);

    Task<bool> ResetForRetryAsync(Guid exportId, Guid recoveryToken, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrganisationDataExportJobView>> GetArtefactCleanupCandidatesAsync(int batchSize, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrganisationDataExportJobView>> GetRecentlyCleanedArtefactsAsync(int batchSize, CancellationToken cancellationToken);

    Task MarkAttemptFilesCleanedAsync(Guid exportId, CancellationToken cancellationToken);

    /// <summary>
    /// Ticket 3K: the cleanup sweep could not finish this terminal export on this run (legal hold, or a
    /// storage listing/delete failure). Push it behind a capped backoff so it yields its batch slot to
    /// older eligible exports and is retried on a later run. Never overwrites the export failure reason.
    /// </summary>
    Task DeferArtefactCleanupAsync(Guid exportId, CancellationToken cancellationToken);

    /// <summary>
    /// Ticket 3K: record the outcome of a late-upload straggler recheck on an already-cleaned terminal
    /// export, advancing the durable recheck cursor so the sweep rotates through every recently-cleaned
    /// export and a failed recheck stays retryable past the 14-day window.
    /// </summary>
    Task RecordLateUploadRecheckAsync(Guid exportId, bool succeeded, CancellationToken cancellationToken);

    /// <summary>
    /// Ticket 3 / Follow-up A: exports that need recovery — Pending rows saved before
    /// <paramref name="pendingQueuedBefore"/> (saved but seemingly never queued) and InProgress rows
    /// whose ownership lease has expired as of <paramref name="leaseExpiredAsOf"/> (worker crashed
    /// mid-build). Healthy in-progress exports keep renewing their lease and are never returned.
    /// </summary>
    Task<IReadOnlyList<OrganisationDataExportJobView>> GetRecoverableAsync(
        DateTimeOffset pendingQueuedBefore,
        DateTimeOffset leaseExpiredAsOf,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OrganisationDataExportJobView>> GetExpiredAsync(CancellationToken cancellationToken);

    Task MarkExpiredAsync(Guid exportId, CancellationToken cancellationToken);
}

public sealed record OrganisationDataExportJobView(
    Guid Id,
    Guid CompanyId,
    string Status,
    string? StorageKey,
    DateTimeOffset? ExpiresAt,
    Guid? RequestedByUserId = null,
    int AttemptCount = 0,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? LastAttemptAt = null,
    Guid? LeaseOwnerToken = null,
    DateTimeOffset? LeaseExpiresAt = null);
