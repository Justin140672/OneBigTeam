using HR.SharedKernel;

namespace HR.Modules.DataImport.Domain;

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

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public DateTimeOffset? ClearedAt { get; private set; }

    public DateTimeOffset? DeletionEligibleAt { get; private set; }

    /// <summary>Ticket 2 optimistic-concurrency token (see <see cref="IVersionedAggregate"/>). Every
    /// save — including the confirming request's <see cref="MarkConfirmed"/> and the reconciliation
    /// job's own updates — advances this automatically via <c>VersionAdvancingSaveChangesInterceptor</c>.
    /// The job uses it to detect, immediately before the destructive storage delete, whether a
    /// request confirmed this intent after the job loaded it — see
    /// <c>PurgeOrphanedImportFileUploadsJob.DeleteConfirmedOrphansAsync</c>.</summary>
    public int Version { get; private set; } = 1;

    public void IncrementVersion() => Version++;

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

    public void MarkConfirmed(DateTimeOffset now) => ConfirmedAt = now;

    public void MarkDeletionEligible(DateTimeOffset now) => DeletionEligibleAt = now;

    /// <summary>Claims this row for a deletion attempt immediately before the destructive storage
    /// call, by touching a field under the optimistic-concurrency token. Callers must persist this
    /// alone (a dedicated <c>SaveChangesAsync</c>) and treat a concurrency conflict as proof a
    /// request confirmed the intent concurrently — and therefore must NOT call storage delete.</summary>
    public void BeginDeletionAttempt(DateTimeOffset now) => LastAttemptedAt = now;

    public void MarkDeleted(DateTimeOffset now)
    {
        DeletedAt = now;
        LastAttemptedAt = now;
    }

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
