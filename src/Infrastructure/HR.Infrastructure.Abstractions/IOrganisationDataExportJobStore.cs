namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Reporting-owned contract that lets the Infrastructure background job read and update a single
/// OrganisationDataExport row without touching ReportingDbContext directly. Implemented by an
/// internal service in HR.Modules.Reporting, DI-registered in ReportingModule.
/// </summary>
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

    /// <summary>
    /// Follow-up A: heartbeat — extend <paramref name="ownerToken"/>'s lease while the build job works.
    /// Returns <c>false</c> when the worker has been superseded or the export is no longer in progress,
    /// signalling the caller to abandon its attempt without completing or overwriting the archive.
    /// </summary>
    Task<bool> RenewLeaseAsync(Guid exportId, Guid ownerToken, CancellationToken cancellationToken);

    /// <summary>
    /// Follow-up A: complete the export. Returns <c>false</c> when <paramref name="ownerToken"/> no
    /// longer owns the lease (a replacement worker has taken over), so a superseded worker cannot
    /// publish a stale archive.
    /// </summary>
    Task<bool> MarkCompletedAsync(Guid exportId, Guid ownerToken, string storageKey, long fileSizeBytes, CancellationToken cancellationToken);

    /// <summary>System/recovery-driven failure — not tied to a worker's ownership lease.</summary>
    Task MarkFailedAsync(Guid exportId, string failureReason, CancellationToken cancellationToken);

    /// <summary>
    /// Follow-up D: worker-driven transient failure. Returns <c>false</c> when <paramref name="ownerToken"/>
    /// no longer owns the lease (a replacement worker has taken over), so a superseded worker cannot
    /// write a failure over an export that has moved on.
    /// </summary>
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

    /// <summary>
    /// Follow-up H: atomically claim recovery ownership of an apparently stalled InProgress export.
    /// Rechecks status and lease expiry under an optimistic-concurrency guard. Returns <c>false</c>
    /// when the export has completed/failed, the original worker renewed its lease, ownership changed,
    /// or another recovery sweep won the race — the caller must then abandon recovery without touching
    /// any files. On success the caller holds the lease and may safely clean up and reset/fail it.
    /// </summary>
    Task<bool> ClaimForRecoveryAsync(Guid exportId, Guid recoveryToken, CancellationToken cancellationToken);

    /// <summary>
    /// Follow-up H: reset a stalled InProgress export to Pending, but only for the recovery sweep that
    /// currently holds the lease via <see cref="ClaimForRecoveryAsync"/>. Returns <c>false</c> if
    /// ownership has since changed.
    /// </summary>
    Task<bool> ResetForRetryAsync(Guid exportId, Guid recoveryToken, CancellationToken cancellationToken);

    /// <summary>
    /// Follow-up I: terminal exports (Failed / Expired / Completed) whose orphan attempt archives have
    /// not yet been swept from storage, oldest first, capped at <paramref name="batchSize"/>.
    /// </summary>
    Task<IReadOnlyList<OrganisationDataExportJobView>> GetArtefactCleanupCandidatesAsync(int batchSize, CancellationToken cancellationToken);

    /// <summary>
    /// Follow-up I: recently-cleaned terminal exports, re-checked in bounded batches so an attempt
    /// archive uploaded by a superseded worker <i>after</i> an earlier sweep is still removed.
    /// </summary>
    Task<IReadOnlyList<OrganisationDataExportJobView>> GetRecentlyCleanedArtefactsAsync(int batchSize, CancellationToken cancellationToken);

    /// <summary>Follow-up I: record that every orphan attempt archive for this terminal export has been removed.</summary>
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

    /// <summary>Completed exports past their expiry, for the recurring purge job.</summary>
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
