namespace HR.Modules.DataImport.Domain;

internal sealed class ImportSession
{
    private ImportSession() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string EntityType { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public ImportStatus Status { get; private set; }
    public int TotalRows { get; private set; }
    public int ProcessedRows { get; private set; }
    public int SuccessfulRows { get; private set; }
    public int FailedRows { get; private set; }
    public Guid InitiatedByUserId { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? ErrorSummary { get; private set; }
    public string StorageKey { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public int Version { get; private set; } = 1;

    public DateTimeOffset? FileDeletedAt { get; private set; }
    public DateTimeOffset? FileDeletionLastAttemptedAt { get; private set; }
    public int FileDeletionAttemptCount { get; private set; }

    public static ImportSession Create(
        Guid id,
        Guid companyId,
        string entityType,
        string fileName,
        int totalRows,
        Guid initiatedByUserId,
        string storageKey,
        string contentType,
        DateTimeOffset now)
    {
        return new ImportSession
        {
            Id = id,
            CompanyId = companyId,
            EntityType = entityType,
            FileName = fileName,
            Status = ImportStatus.Pending,
            TotalRows = totalRows,
            ProcessedRows = 0,
            SuccessfulRows = 0,
            FailedRows = 0,
            InitiatedByUserId = initiatedByUserId,
            StorageKey = storageKey,
            ContentType = contentType,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Start(DateTimeOffset now)
    {
        Status = ImportStatus.Processing;
        StartedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// OBT-REM-06: atomically claims the session for a confirm/create run. Combined with the
    /// row-version concurrency token, only one caller can transition the session into
    /// <see cref="ImportStatus.Processing"/>; a competing confirmation loses the optimistic-
    /// concurrency check and is rejected.
    /// </summary>
    public void ClaimForConfirmation(DateTimeOffset now)
    {
        Status = ImportStatus.Processing;
        StartedAt = now;
        CompletedAt = null;
        UpdatedAt = now;
        Version++;
    }

    public void Complete(int successfulRows, int failedRows, DateTimeOffset now)
    {
        SuccessfulRows = successfulRows;
        FailedRows = failedRows;
        ProcessedRows = successfulRows + failedRows;
        CompletedAt = now;
        Status = failedRows == 0 ? ImportStatus.Completed : ImportStatus.CompletedWithErrors;
        UpdatedAt = now;
    }

    public void Validate(int successfulRows, int failedRows, DateTimeOffset now)
    {
        SuccessfulRows = successfulRows;
        FailedRows = failedRows;
        ProcessedRows = successfulRows + failedRows;
        CompletedAt = now;
        Status = successfulRows > 0 ? ImportStatus.Validated : ImportStatus.CompletedWithErrors;
        UpdatedAt = now;
    }

    public void Confirm(int createdCount, int failedCount, DateTimeOffset now)
    {
        SuccessfulRows = createdCount;
        FailedRows = failedCount;
        ProcessedRows = createdCount + failedCount;
        CompletedAt = now;
        Status = failedCount == 0 ? ImportStatus.Imported : ImportStatus.CompletedWithErrors;
        UpdatedAt = now;
        Version++;
    }

    public void Fail(string errorSummary, DateTimeOffset now)
    {
        Status = ImportStatus.Failed;
        CompletedAt = now;
        ErrorSummary = errorSummary;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        Status = ImportStatus.Cancelled;
        CompletedAt = now;
        UpdatedAt = now;
    }

    public void MarkFileDeleted(DateTimeOffset now)
    {
        FileDeletedAt = now;
        FileDeletionLastAttemptedAt = now;
    }

    public void RecordFileDeletionAttemptFailed(DateTimeOffset now)
    {
        FileDeletionLastAttemptedAt = now;
        FileDeletionAttemptCount++;
    }
}
