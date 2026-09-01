using HR.SharedKernel;

namespace HR.Modules.Reporting.Domain;

/// <summary>
/// Story 2 (customer organisation data export before account closure): a single request by a
/// company administrator to generate a full, downloadable ZIP export of their organisation's data.
/// The lifecycle is Pending -> InProgress -> Completed|Failed, with Completed exports later
/// transitioning to Expired by the recurring purge job once <see cref="ExpiresAt"/> passes.
/// All state transitions are guarded and return a <see cref="Result"/>.
/// </summary>
internal sealed class OrganisationDataExport
{
    public const string StatusPending = "Pending";
    public const string StatusInProgress = "InProgress";
    public const string StatusCompleted = "Completed";
    public const string StatusFailed = "Failed";
    public const string StatusExpired = "Expired";

    /// <summary>Download availability window after completion.</summary>
    public const int RetentionDays = 7;

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
        return Result.Success();
    }

    public Result MarkCompleted(string storageKey, long fileSizeBytes, DateTimeOffset now)
    {
        if (Status != StatusInProgress)
            return Result.Failure(Error.Conflict($"Export '{Id}' cannot complete from status '{Status}'."));

        if (string.IsNullOrWhiteSpace(storageKey))
            return Result.Failure(Error.Validation("A completed export requires a storage key."));

        Status = StatusCompleted;
        StorageKey = storageKey;
        FileSizeBytes = fileSizeBytes;
        CompletedAt = now;
        ExpiresAt = now.AddDays(RetentionDays);
        return Result.Success();
    }

    public Result MarkFailed(string failureReason, DateTimeOffset now)
    {
        if (Status is StatusCompleted or StatusExpired)
            return Result.Failure(Error.Conflict($"Export '{Id}' cannot fail from status '{Status}'."));

        Status = StatusFailed;
        FailureReason = string.IsNullOrWhiteSpace(failureReason) ? "Export could not be generated." : failureReason;
        CompletedAt = now;
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
