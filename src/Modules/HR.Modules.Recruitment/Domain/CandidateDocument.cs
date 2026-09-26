namespace HR.Modules.Recruitment.Domain;

internal sealed class CandidateDocument
{
    private CandidateDocument() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CandidateId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public CandidateDocumentKind Kind { get; private set; } = CandidateDocumentKind.Other;
    public string FileName { get; private set; } = string.Empty;
    public long FileSize { get; private set; }
    public string ContentType { get; private set; } = string.Empty;
    public string StorageKey { get; private set; } = string.Empty;
    public Guid UploadedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // ── Malware scanning ([P1] candidate CV scan/quarantine) ──────────────────────────────────
    // A document is only ever downloadable once ScanStatus == Clean. ScanAttemptCount doubles as the
    // optimistic-concurrency token for scan claims (see CandidateDocumentConfiguration), so two
    // workers can never both claim the same attempt.

    /// <summary>Hard ceiling on scan attempts (job retries and reconciliation re-dispatch combined).</summary>
    public const int MaxScanAttempts = 5;

    /// <summary>A Scanning claim older than this is treated as abandoned (lost job / crashed worker).</summary>
    public static readonly TimeSpan ScanLeaseDuration = TimeSpan.FromMinutes(15);

    public CandidateDocumentScanStatus ScanStatus { get; private set; } = CandidateDocumentScanStatus.Pending;
    public int ScanAttemptCount { get; private set; }
    public DateTimeOffset? ScanLastAttemptAt { get; private set; }
    public DateTimeOffset? ScanNextAttemptAt { get; private set; }
    public DateTimeOffset? ScanCompletedAt { get; private set; }
    public string? ScanFailureReason { get; private set; }

    public bool IsDownloadable => ScanStatus == CandidateDocumentScanStatus.Clean;

    public bool IsScanTerminal => ScanStatus is CandidateDocumentScanStatus.Clean
        or CandidateDocumentScanStatus.Infected
        or CandidateDocumentScanStatus.Failed;

    public bool HasRemainingScanAttempts => ScanAttemptCount < MaxScanAttempts;

    public bool IsScanLeaseExpired(DateTimeOffset now) =>
        ScanStatus == CandidateDocumentScanStatus.Scanning
        && (ScanLastAttemptAt is null || ScanLastAttemptAt.Value + ScanLeaseDuration <= now);

    /// <summary>
    /// True when a scan attempt may start now: a Pending document whose retry back-off (if any) has
    /// elapsed, or a Scanning document whose claim was abandoned.
    /// </summary>
    public bool CanBeginScanAttempt(DateTimeOffset now) => ScanStatus switch
    {
        CandidateDocumentScanStatus.Pending => ScanNextAttemptAt is null || ScanNextAttemptAt <= now,
        CandidateDocumentScanStatus.Scanning => IsScanLeaseExpired(now),
        _ => false,
    };

    /// <summary>Claims a scan attempt. Caller must check <see cref="CanBeginScanAttempt"/> and
    /// <see cref="HasRemainingScanAttempts"/> first.</summary>
    public void BeginScanAttempt(DateTimeOffset now)
    {
        if (!CanBeginScanAttempt(now))
            throw new InvalidOperationException($"A scan attempt cannot start while the document is {ScanStatus}.");
        if (!HasRemainingScanAttempts)
            throw new InvalidOperationException("The document has no remaining scan attempts.");

        ScanStatus = CandidateDocumentScanStatus.Scanning;
        ScanAttemptCount++;
        ScanLastAttemptAt = now;
        ScanNextAttemptAt = null;
    }

    public void MarkScanClean(DateTimeOffset now)
    {
        EnsureScanning();
        ScanStatus = CandidateDocumentScanStatus.Clean;
        ScanCompletedAt = now;
        ScanFailureReason = null;
        ScanNextAttemptAt = null;
    }

    public void MarkScanInfected(string? threatName, DateTimeOffset now)
    {
        EnsureScanning();
        ScanStatus = CandidateDocumentScanStatus.Infected;
        ScanCompletedAt = now;
        ScanFailureReason = CandidateDocumentScanFailureReasons.SanitiseThreatName(threatName);
        ScanNextAttemptAt = null;
    }

    /// <summary>
    /// Records a failed attempt (scanner or storage outage). Returns the document to Pending with a
    /// back-off when attempts remain; otherwise it becomes terminally Failed. Never marks Clean.
    /// </summary>
    public void RecordFailedScanAttempt(string safeReason, DateTimeOffset now, DateTimeOffset nextAttemptAt)
    {
        EnsureScanning();
        ScanFailureReason = safeReason;

        if (HasRemainingScanAttempts)
        {
            ScanStatus = CandidateDocumentScanStatus.Pending;
            ScanNextAttemptAt = nextAttemptAt;
        }
        else
        {
            ScanStatus = CandidateDocumentScanStatus.Failed;
            ScanCompletedAt = now;
            ScanNextAttemptAt = null;
        }
    }

    /// <summary>
    /// Reconciliation: a claim whose lease expired (the worker was lost) is released back to Pending
    /// for immediate re-dispatch, or terminally Failed once the attempt budget is spent.
    /// </summary>
    public void ReleaseAbandonedScan(DateTimeOffset now)
    {
        if (!IsScanLeaseExpired(now))
            throw new InvalidOperationException("Only an expired scan claim can be released.");

        RecordFailedScanAttempt(CandidateDocumentScanFailureReasons.ScanAbandoned, now, now);
    }

    /// <summary>A Pending document that has already used every attempt is closed off as Failed
    /// rather than scanned again.</summary>
    public void MarkScanRetryLimitReached(DateTimeOffset now)
    {
        if (ScanStatus != CandidateDocumentScanStatus.Pending || HasRemainingScanAttempts)
            throw new InvalidOperationException("Only a Pending document with no remaining attempts can be closed off.");

        ScanStatus = CandidateDocumentScanStatus.Failed;
        ScanCompletedAt = now;
        ScanFailureReason ??= CandidateDocumentScanFailureReasons.RetryLimitReached;
        ScanNextAttemptAt = null;
    }

    private void EnsureScanning()
    {
        if (ScanStatus != CandidateDocumentScanStatus.Scanning)
            throw new InvalidOperationException($"Scan result cannot be recorded while the document is {ScanStatus}.");
    }

    public static CandidateDocument Create(
        Guid id,
        Guid companyId,
        Guid candidateId,
        string title,
        string fileName,
        long fileSize,
        string contentType,
        string storageKey,
        Guid uploadedBy,
        DateTimeOffset now,
        CandidateDocumentKind kind = CandidateDocumentKind.Other) => new()
    {
        Id          = id,
        CompanyId   = companyId,
        CandidateId = candidateId,
        Title       = title.Trim(),
        Kind        = kind,
        FileName    = fileName.Trim(),
        FileSize    = fileSize,
        ContentType = contentType.Trim(),
        StorageKey  = storageKey.Trim(),
        UploadedBy  = uploadedBy,
        CreatedAt   = now,
        // Every new upload is untrusted until scanned — see ScanCandidateDocumentJob.
        ScanStatus       = CandidateDocumentScanStatus.Pending,
        ScanAttemptCount = 0,
    };
}
