namespace HR.Modules.Recruitment.Domain;

/// <summary>
/// Malware-scan lifecycle of an uploaded candidate document. Every document starts (or, for rows that
/// existed before scanning was introduced, is backfilled to) <see cref="Pending"/>; only
/// <see cref="Clean"/> documents may ever be handed out through a signed download URL.
/// Persisted as its name (see CandidateDocumentConfiguration), so values must never be renamed.
/// </summary>
internal enum CandidateDocumentScanStatus
{
    /// <summary>Waiting for a scan attempt (new upload, backfilled row, or a retry after a failed attempt).</summary>
    Pending = 0,

    /// <summary>A scan attempt has claimed the document and is in progress.</summary>
    Scanning = 1,

    /// <summary>The scanner inspected the stored bytes and found no threat. The only downloadable state.</summary>
    Clean = 2,

    /// <summary>The scanner found a threat. The blob is quarantined (durably deleted); the row is kept as evidence.</summary>
    Infected = 3,

    /// <summary>Every bounded scan attempt failed (scanner/storage outage). Never downloadable.</summary>
    Failed = 4,
}
