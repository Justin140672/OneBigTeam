using HR.SharedKernel;

namespace HR.Modules.DataImport.Domain;

/// <summary>
/// Follow-up review finding: this row is now a durable "upload intent", persisted BEFORE the blob is
/// uploaded (see Features/UploadImportFile/Handler.cs), not only after a failure is detected as
/// compensation. Previously durable intent was written only once a failure occurred, so a process
/// crash immediately after a successful upload — with the DB unreachable for both the session save
/// AND the compensating delete/record-write — left the blob with zero durable trace anywhere. Because
/// this row now exists before the upload is even attempted, that failure mode is no longer possible:
/// the row is always there, no matter what happens afterward or when the process dies.
///
/// Lifecycle:
///  - Created (CreatedAt set, <see cref="ConfirmedAt"/> null) immediately before the storage upload
///    call.
///  - On a successful upload AND successful <see cref="ImportSession"/> save, marked
///    <see cref="MarkConfirmed"/> in the SAME SaveChangesAsync call as the session insert — the
///    intent is fulfilled, no cleanup will ever be needed.
///  - If the session save fails (with or without a successful upload), this row is left unconfirmed
///    and durable — Jobs/PurgeOrphanedImportFileUploadsJob's reconciliation sweep is what
///    authoritatively resolves it: after a grace period, it checks whether the object actually
///    exists in storage. If it does, it is deleted (<see cref="MarkDeleted"/>, reusing the existing
///    retry/attempt-count idiom). If it does not (the process crashed before the upload itself
///    completed), the intent is simply cleared (<see cref="MarkClearedNeverUploaded"/>) — there was
///    never anything to delete.
///
/// Mirrors the retry/attempt-count idiom already used on <see cref="ImportSession"/>
/// (FileDeletedAt/FileDeletionLastAttemptedAt/FileDeletionAttemptCount, security review finding #2)
/// rather than the heavier claim/lease pattern used by Recruitment's
/// CandidateDocumentDeletionOperation — this workflow has no equivalent "purge" concurrency to guard
/// against, so a simple attempt counter with a retry-grace window is sufficient.
/// </summary>
internal sealed class OrphanedImportFileUpload : IVersionedAggregate
{
    private OrphanedImportFileUpload() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string StorageKey { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset? LastAttemptedAt { get; private set; }
    public int AttemptCount { get; private set; }

    /// <summary>Set when the owning business record was durably saved (in the same transaction as
    /// this flag being set) — the intent is fulfilled and no cleanup will ever be needed for this
    /// row again.</summary>
    public DateTimeOffset? ConfirmedAt { get; private set; }

    /// <summary>Set by the reconciliation sweep when it determined the blob was never actually
    /// uploaded (the process crashed before the upload call completed) — nothing to delete, the
    /// intent is simply resolved.</summary>
    public DateTimeOffset? ClearedAt { get; private set; }

    /// <summary>
    /// Explicit state transition (P1 fix, Sept 2026): null until <see cref="ResolveUnconfirmedIntentsAsync"/>-
    /// equivalent logic in the reconciliation job proves, past the upload-intent grace period, that
    /// this intent is unconfirmed AND has a real object in storage. Only once this is set may the
    /// deletion phase consider the row at all — a row created moments ago (still mid-upload) can
    /// never reach <c>DeleteAsync</c>, because nothing but the grace-gated resolution step sets this.
    /// </summary>
    public DateTimeOffset? DeletionEligibleAt { get; private set; }

    /// <summary>Ticket 2 optimistic-concurrency token (see <see cref="IVersionedAggregate"/>). Every
    /// save — including the confirming request's <see cref="MarkConfirmed"/> and the reconciliation
    /// job's own updates — advances this automatically via <c>VersionAdvancingSaveChangesInterceptor</c>.
    /// The job uses it to detect, immediately before the destructive storage delete, whether a
    /// request confirmed this intent after the job loaded it — see
    /// <c>PurgeOrphanedImportFileUploadsJob.DeleteConfirmedOrphansAsync</c>.</summary>
    public int Version { get; private set; } = 1;

    public void IncrementVersion() => Version++;

    /// <summary>
    /// Reserves a durable upload intent for <paramref name="storageKey"/>. Must be persisted
    /// (SaveChangesAsync) BEFORE the corresponding storage upload call is made — that ordering is
    /// the entire point: durability no longer depends on any compensation path succeeding.
    /// </summary>
    public static OrphanedImportFileUpload CreateReserved(
        Guid id, Guid companyId, string storageKey, DateTimeOffset now)
    {
        return new OrphanedImportFileUpload
        {
            Id = id,
            CompanyId = companyId,
            StorageKey = storageKey,
            CreatedAt = now,
        };
    }

    /// <summary>The owning business record was durably saved — this intent is fulfilled and will
    /// never need cleanup. Intended to be saved in the same SaveChangesAsync call as that business
    /// record's insert.</summary>
    public void MarkConfirmed(DateTimeOffset now) => ConfirmedAt = now;

    /// <summary>Reconciliation determined, past the grace period, that this intent is unconfirmed
    /// and has a real object in storage — the only way a row may become a candidate for
    /// <see cref="MarkDeleted"/>. Must never be called before the grace-period cutoff.</summary>
    public void MarkDeletionEligible(DateTimeOffset now) => DeletionEligibleAt = now;

    /// <summary>Claims this row for a deletion attempt immediately before the destructive storage
    /// call, by touching a field under the optimistic-concurrency token. Callers must persist this
    /// alone (a dedicated <c>SaveChangesAsync</c>) and treat a concurrency conflict as proof a
    /// request confirmed the intent concurrently — and therefore must NOT call storage delete.</summary>
    public void BeginDeletionAttempt(DateTimeOffset now) => LastAttemptedAt = now;

    /// <summary>Idempotent: safe to call more than once — callers should skip deletion entirely
    /// once <see cref="DeletedAt"/> is already set.</summary>
    public void MarkDeleted(DateTimeOffset now)
    {
        DeletedAt = now;
        LastAttemptedAt = now;
    }

    /// <summary>Reconciliation determined the blob was never actually uploaded (a crash occurred
    /// before the upload call completed) — there is nothing to delete, so the intent is simply
    /// resolved.</summary>
    public void MarkClearedNeverUploaded(DateTimeOffset now)
    {
        ClearedAt = now;
        LastAttemptedAt = now;
    }

    public void RecordAttemptFailed(DateTimeOffset now)
    {
        LastAttemptedAt = now;
        AttemptCount++;
    }
}
