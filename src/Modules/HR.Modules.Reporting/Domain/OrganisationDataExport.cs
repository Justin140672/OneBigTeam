using HR.SharedKernel;

namespace HR.Modules.Reporting.Domain;

/// <summary>
/// Story 2 (customer organisation data export before account closure): a single request by a
/// company administrator to generate a full, downloadable ZIP export of their organisation's data.
/// The lifecycle is Pending -> InProgress -> Completed|Failed, with Completed exports later
/// transitioning to Expired by the recurring purge job once <see cref="ExpiresAt"/> passes.
/// All state transitions are guarded and return a <see cref="Result"/>.
/// </summary>
internal sealed class OrganisationDataExport : IVersionedAggregate
{
    public const string StatusPending = "Pending";
    public const string StatusInProgress = "InProgress";
    public const string StatusCompleted = "Completed";
    public const string StatusFailed = "Failed";
    public const string StatusExpired = "Expired";

    /// <summary>Download availability window after completion.</summary>
    public const int RetentionDays = 7;

    /// <summary>
    /// Ticket 3: how many times the build job may run for a single export (initial attempt plus
    /// interruption-recovery retries) before the recovery sweep gives up and fails it.
    /// </summary>
    public const int MaxAttempts = 3;

    /// <summary>
    /// Follow-up A: how long a worker's ownership lease on an in-progress export lasts before the
    /// recovery sweep may reclaim it. The build job renews (heartbeats) the lease while it works, so a
    /// healthy long-running export is never reclaimed; only a genuinely abandoned lease expires.
    /// </summary>
    public const int LeaseDurationMinutes = 15;

    private OrganisationDataExport() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? RequestedByUserId { get; private set; }
    public string? RequestedByDisplayName { get; private set; }
    public string Status { get; private set; } = StatusPending;
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public string? StorageKey { get; private set; }
    public long? FileSizeBytes { get; private set; }
    public string? FailureReason { get; private set; }
    public int DownloadCount { get; private set; }
    public DateTimeOffset? LastDownloadedAt { get; private set; }
    public Guid? LastDownloadedByUserId { get; private set; }

    /// <summary>Ticket 3: number of times the build job has started processing this export.</summary>
    public int AttemptCount { get; private set; }

    /// <summary>Ticket 3: when the build job most recently began an attempt — drives stale detection.</summary>
    public DateTimeOffset? LastAttemptAt { get; private set; }

    /// <summary>Ticket 3: how many expected documents were missing from storage when the export failed.</summary>
    public int MissingDocumentCount { get; private set; }

    /// <summary>
    /// Follow-up I: when the retryable artefact-cleanup job last confirmed that every orphan attempt
    /// archive for this (terminal) export had been removed from storage. Null until cleaned; a failed
    /// cleanup leaves it null so the next run retries.
    /// </summary>
    public DateTimeOffset? AttemptFilesCleanedAt { get; private set; }

    /// <summary>
    /// Ticket 3K: how long a cleaned terminal export stays eligible for the late-upload straggler
    /// recheck. An archive uploaded by a superseded worker after an earlier successful sweep must
    /// still be found and removed within this window.
    /// </summary>
    public const int LateUploadRecheckWindowDays = 14;

    /// <summary>
    /// Ticket 3K: durable cursor for the retryable artefact-cleanup sweep. When a candidate cannot be
    /// processed on a run (legal hold, listing/delete failure) it is deferred to this time and yields
    /// its batch slot, so a blocked entry can never permanently starve older eligible exports. Null
    /// means "eligible now"; reset to null on a successful clean.
    /// </summary>
    public DateTimeOffset? ArtefactCleanupNextAttemptAt { get; private set; }

    /// <summary>Ticket 3K: number of deferred artefact-cleanup attempts; drives the capped backoff.</summary>
    public int ArtefactCleanupAttemptCount { get; private set; }

    /// <summary>
    /// Ticket 3K: durable cursor for the late-upload straggler recheck. Set to a future time after every
    /// processed recheck (success or failure) so the entry yields its slot and the sweep rotates through
    /// all recently-cleaned exports. A failed recheck keeps this non-null so the entry stays retryable
    /// even after <see cref="AttemptFilesCleanedAt"/> ages past the 14-day window.
    /// </summary>
    public DateTimeOffset? LateUploadRecheckNextAt { get; private set; }

    /// <summary>Ticket 3K: number of failed late-upload rechecks since the last success; drives the capped backoff.</summary>
    public int LateUploadRecheckAttemptCount { get; private set; }

    /// <summary>Follow-up A: opaque token identifying the worker that currently owns this in-progress export.</summary>
    public Guid? LeaseOwnerToken { get; private set; }

    /// <summary>Follow-up A: when the current worker acquired its ownership lease.</summary>
    public DateTimeOffset? LeaseAcquiredAt { get; private set; }

    /// <summary>Follow-up A: when the current worker's ownership lease expires and recovery may reclaim the export.</summary>
    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    /// <summary>Ticket 2 optimistic-concurrency token — guards the Pending -&gt; InProgress claim.</summary>
    public int Version { get; private set; } = 1;

    public void IncrementVersion() => Version++;

    public bool CanAttemptAgain => AttemptCount < MaxAttempts;

    /// <summary>Follow-up H/I: the export has reached a terminal state and will not do any more work.</summary>
    public bool IsTerminal => Status is StatusCompleted or StatusFailed or StatusExpired;

    /// <summary>Follow-up A: true when no worker holds a live ownership lease (never leased, or lease expired).</summary>
    public bool IsLeaseExpired(DateTimeOffset now) => LeaseExpiresAt is not { } expires || expires <= now;

    private void ClearLease()
    {
        LeaseOwnerToken = null;
        LeaseAcquiredAt = null;
        LeaseExpiresAt = null;
    }

    public bool IsDownloadable(DateTimeOffset now) =>
        Status == StatusCompleted && ExpiresAt is { } expires && expires > now;

    public static OrganisationDataExport Create(
        Guid companyId,
        Guid? requestedByUserId,
        string? requestedByDisplayName,
        DateTimeOffset now)
    {
        return new OrganisationDataExport
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            RequestedByUserId = requestedByUserId,
            RequestedByDisplayName = requestedByDisplayName,
            Status = StatusPending,
            RequestedAt = now,
            DownloadCount = 0,
        };
    }

    public Result MarkInProgress(DateTimeOffset now)
    {
        if (Status != StatusPending)
            return Result.Failure(Error.Conflict($"Export '{Id}' cannot start from status '{Status}'."));

        Status = StatusInProgress;
        StartedAt = now;
        LastAttemptAt = now;
        AttemptCount++;
        return Result.Success();
    }

    /// <summary>
    /// Ticket 3 / Follow-up A: claim this export for a build-job attempt and take out an ownership
    /// lease. Allowed from Pending (first run) and from InProgress only when the previous worker's
    /// lease has expired (a genuine interruption) — never while another worker still holds a live
    /// lease. The attempt limit is enforced here so a worker can never start a fourth attempt.
    /// </summary>
    public Result BeginAttempt(Guid ownerToken, DateTimeOffset now)
    {
        if (ownerToken == Guid.Empty)
            return Result.Failure(Error.Validation("A build-job attempt requires an owner token."));

        if (Status is not (StatusPending or StatusInProgress))
            return Result.Failure(Error.Conflict($"Export '{Id}' cannot start from status '{Status}'."));

        if (Status == StatusInProgress && !IsLeaseExpired(now))
            return Result.Failure(Error.Conflict($"Export '{Id}' is already owned by another worker until {LeaseExpiresAt:o}."));

        if (!CanAttemptAgain)
            return Result.Failure(Error.Conflict($"Export '{Id}' has exhausted its {MaxAttempts} attempts."));

        Status = StatusInProgress;
        StartedAt ??= now;
        LastAttemptAt = now;
        AttemptCount++;
        LeaseOwnerToken = ownerToken;
        LeaseAcquiredAt = now;
        LeaseExpiresAt = now.AddMinutes(LeaseDurationMinutes);
        return Result.Success();
    }

    /// <summary>
    /// Follow-up A: heartbeat — extend the current owner's lease while the build job is still working.
    /// Fails if the caller is not the current owner (it has been superseded) or the export is no
    /// longer in progress, signalling the worker to abandon its attempt.
    /// </summary>
    public Result RenewLease(Guid ownerToken, DateTimeOffset now)
    {
        if (Status != StatusInProgress || LeaseOwnerToken != ownerToken)
            return Result.Failure(Error.Conflict($"Export '{Id}' is no longer owned by worker '{ownerToken}'."));

        LeaseExpiresAt = now.AddMinutes(LeaseDurationMinutes);
        return Result.Success();
    }

    /// <summary>Ticket 3: return a stalled InProgress export to the queue for another attempt; releases the lease.</summary>
    public Result ResetForRetry(DateTimeOffset now)
    {
        if (Status != StatusInProgress)
            return Result.Failure(Error.Conflict($"Export '{Id}' cannot be reset from status '{Status}'."));

        Status = StatusPending;
        StartedAt = null;
        ClearLease();
        return Result.Success();
    }

    /// <summary>
    /// Follow-up H: atomically claim recovery ownership of an apparently stalled InProgress export
    /// before any file deletion or reset. Rechecks status and lease expiry: the claim is refused if
    /// the export has completed/failed, if the original worker has renewed its lease, or if another
    /// recovery sweep already took ownership. On success the recovery sweep holds the lease, so a
    /// resurrected original worker can no longer renew, complete or fail the export.
    /// </summary>
    public Result ClaimForRecovery(Guid recoveryToken, DateTimeOffset now)
    {
        if (recoveryToken == Guid.Empty)
            return Result.Failure(Error.Validation("Recovery requires an owner token."));

        if (Status != StatusInProgress)
            return Result.Failure(Error.Conflict($"Export '{Id}' is no longer recoverable (status '{Status}')."));

        if (!IsLeaseExpired(now))
            return Result.Failure(Error.Conflict($"Export '{Id}' lease is still live until {LeaseExpiresAt:o}; recovery abandoned."));

        LeaseOwnerToken = recoveryToken;
        LeaseAcquiredAt = now;
        LeaseExpiresAt = now.AddMinutes(LeaseDurationMinutes);
        return Result.Success();
    }

    /// <summary>
    /// Follow-up H: return a stalled InProgress export to the queue, but only for the recovery sweep
    /// that currently holds the lease (see <see cref="ClaimForRecovery"/>).
    /// </summary>
    public Result ResetForRetry(Guid recoveryToken, DateTimeOffset now)
    {
        if (Status != StatusInProgress)
            return Result.Failure(Error.Conflict($"Export '{Id}' cannot be reset from status '{Status}'."));

        if (LeaseOwnerToken != recoveryToken)
            return Result.Failure(Error.Conflict($"Export '{Id}' is no longer owned by recovery worker '{recoveryToken}'."));

        Status = StatusPending;
        StartedAt = null;
        ClearLease();
        return Result.Success();
    }

    /// <summary>
    /// Follow-up I: record that the retryable artefact-cleanup job has removed every orphan attempt
    /// archive for this terminal export. Only valid once the export has finished.
    /// </summary>
    public Result MarkAttemptFilesCleaned(DateTimeOffset now)
    {
        if (!IsTerminal)
            return Result.Failure(Error.Conflict($"Export '{Id}' attempt files cannot be cleaned from status '{Status}'."));

        AttemptFilesCleanedAt = now;
        ArtefactCleanupNextAttemptAt = null;
        ArtefactCleanupAttemptCount = 0;
        return Result.Success();
    }

    /// <summary>
    /// Ticket 3K: the artefact-cleanup sweep could not finish this terminal export on this run (company
    /// under a legal hold, or a storage listing/delete failure). Push it behind a capped exponential
    /// backoff so it yields its batch slot to older eligible exports and is retried on a later run. The
    /// original export <see cref="FailureReason"/> is never touched.
    /// </summary>
    public Result DeferArtefactCleanup(DateTimeOffset now)
    {
        if (!IsTerminal)
            return Result.Failure(Error.Conflict($"Export '{Id}' artefact cleanup cannot be deferred from status '{Status}'."));

        if (AttemptFilesCleanedAt is not null)
            return Result.Failure(Error.Conflict($"Export '{Id}' artefact cleanup is already complete."));

        ArtefactCleanupAttemptCount++;
        ArtefactCleanupNextAttemptAt = now.Add(Backoff(ArtefactCleanupAttemptCount));
        return Result.Success();
    }

    /// <summary>
    /// Ticket 3K: record the outcome of a late-upload straggler recheck on an already-cleaned terminal
    /// export. Every processed recheck sets <see cref="LateUploadRecheckNextAt"/> to a future time so the
    /// entry yields its slot and the sweep rotates through every recently-cleaned export. A failed
    /// recheck keeps the entry retryable indefinitely; a successful recheck stops rechecking once the
    /// export has aged past the 14-day window.
    /// </summary>
    public Result RecordLateUploadRecheck(bool succeeded, DateTimeOffset now)
    {
        if (AttemptFilesCleanedAt is not { } cleanedAt)
            return Result.Failure(Error.Conflict($"Export '{Id}' has no completed cleanup to recheck."));

        if (succeeded)
        {
            LateUploadRecheckAttemptCount = 0;
            LateUploadRecheckNextAt = cleanedAt >= now.AddDays(-LateUploadRecheckWindowDays)
                ? now.AddDays(1)
                : null;
        }
        else
        {
            LateUploadRecheckAttemptCount++;
            LateUploadRecheckNextAt = now.Add(Backoff(LateUploadRecheckAttemptCount));
        }

        return Result.Success();
    }

    /// <summary>Ticket 3K: capped exponential backoff (15 min doubling, ceiling 24 h) for durable retry cursors.</summary>
    private static TimeSpan Backoff(int attemptCount)
    {
        var exponent = Math.Max(0, attemptCount - 1);
        var minutes = 15d * Math.Pow(2, Math.Min(exponent, 10));
        return TimeSpan.FromMinutes(Math.Min(minutes, 1440d));
    }

    /// <summary>
    /// Ticket 3: fail the export because one or more expected documents were genuinely missing from
    /// storage. The Documents-module records themselves are never touched.
    /// </summary>
    public Result MarkFailedDueToMissingDocuments(Guid ownerToken, int missingCount, DateTimeOffset now)
    {
        // Ticket 3J: a worker-owned failure is only valid while THIS worker still holds the live
        // lease. After a takeover or a recovery reset (Status left InProgress, or a new owner token)
        // the superseded worker must not be able to fail the row — including during the Pending
        // window after a recovery reset.
        if (Status != StatusInProgress || LeaseOwnerToken != ownerToken)
            return Result.Failure(Error.Conflict($"Export '{Id}' is no longer owned by worker '{ownerToken}'."));

        Status = StatusFailed;
        ClearLease();
        MissingDocumentCount = missingCount < 0 ? 0 : missingCount;
        FailureReason = missingCount == 1
            ? "1 expected document could not be retrieved from storage."
            : $"{missingCount} expected documents could not be retrieved from storage.";
        CompletedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Follow-up A: complete the export. Rejected when the caller no longer owns the lease (a
    /// replacement worker has taken over) so a superseded worker can never publish a stale archive or
    /// overwrite the completed one.
    /// </summary>
    public Result MarkCompleted(Guid ownerToken, string storageKey, long fileSizeBytes, DateTimeOffset now)
    {
        if (Status != StatusInProgress)
            return Result.Failure(Error.Conflict($"Export '{Id}' cannot complete from status '{Status}'."));

        if (LeaseOwnerToken != ownerToken)
            return Result.Failure(Error.Conflict($"Export '{Id}' is no longer owned by worker '{ownerToken}'."));

        if (string.IsNullOrWhiteSpace(storageKey))
            return Result.Failure(Error.Validation("A completed export requires a storage key."));

        Status = StatusCompleted;
        StorageKey = storageKey;
        FileSizeBytes = fileSizeBytes;
        CompletedAt = now;
        ExpiresAt = now.AddDays(RetentionDays);
        ClearLease();
        return Result.Success();
    }

    /// <summary>
    /// Follow-up D: worker-driven transient failure. Rejected when the caller no longer owns the
    /// lease (it has been superseded by a replacement worker) so a stale worker can never overwrite
    /// another worker's outcome or fail an export that has moved on.
    /// </summary>
    public Result MarkFailed(Guid ownerToken, string failureReason, DateTimeOffset now)
    {
        // Ticket 3J: worker-owned failure requires this worker to still hold the live lease. A
        // superseded worker (takeover, or recovery reset that left the row Pending) is locked out.
        if (Status != StatusInProgress || LeaseOwnerToken != ownerToken)
            return Result.Failure(Error.Conflict($"Export '{Id}' is no longer owned by worker '{ownerToken}'."));

        return MarkFailed(failureReason, now);
    }

    public Result MarkFailed(string failureReason, DateTimeOffset now)
    {
        if (Status is StatusCompleted or StatusExpired)
            return Result.Failure(Error.Conflict($"Export '{Id}' cannot fail from status '{Status}'."));

        Status = StatusFailed;
        FailureReason = string.IsNullOrWhiteSpace(failureReason) ? "Export could not be generated." : failureReason;
        CompletedAt = now;
        ClearLease();
        return Result.Success();
    }

    public Result MarkExpired(DateTimeOffset now)
    {
        if (Status != StatusCompleted)
            return Result.Failure(Error.Conflict($"Export '{Id}' cannot expire from status '{Status}'."));

        Status = StatusExpired;
        StorageKey = null;
        return Result.Success();
    }

    public Result RecordDownload(Guid? userId, DateTimeOffset now)
    {
        if (Status != StatusCompleted)
            return Result.Failure(Error.Conflict($"Export '{Id}' is not available for download (status '{Status}')."));

        if (ExpiresAt is { } expires && expires <= now)
            return Result.Failure(Error.Conflict($"Export '{Id}' has expired."));

        DownloadCount++;
        LastDownloadedAt = now;
        LastDownloadedByUserId = userId;
        return Result.Success();
    }
}
