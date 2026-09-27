namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Implemented by the module that owns a <see cref="LocalStorageBuckets"/> bucket. Consulted by the
/// dev-only local-storage delivery route on every request, AFTER the URL signature has been
/// validated, to re-confirm against the database that <paramref name="storageKey"/> still belongs to
/// a live record and — for malware-scanned files — that the record's scan status is Clean. This is
/// what stops a signed URL from outliving the decision that minted it (record deleted/archived, scan
/// flipped to Infected) and what stops an orphaned physical file from ever being served.
///
/// Implementations must mirror the record/scan predicate of the owning module's own authorised
/// download handler(s), minus the caller-identity checks those handlers already made before the URL
/// was signed. Never return true for a key without a matching record.
/// </summary>
public interface ILocalStorageObjectResolver
{
    /// <summary>The <see cref="LocalStorageBuckets"/> bucket this resolver is authoritative for.</summary>
    string Bucket { get; }

    Task<bool> IsServableAsync(string storageKey, CancellationToken cancellationToken);
}
