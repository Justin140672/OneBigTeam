namespace HR.Infrastructure.Abstractions;

/// <summary>
/// The Development/automated-test local-storage buckets (the temp-directory fallbacks used when the
/// matching Supabase storage section is absent) that the dev-only
/// <c>GET /api/dev/local-storage/{bucket}/{*key}</c> route in HR.Api can serve, together with each
/// bucket's physical root. Single source of truth shared by every <c>Local*StorageService</c> and the
/// route, so the two can never disagree about where a bucket lives.
///
/// Serving is never inferred from the bucket + key alone: every URL must carry a valid
/// <see cref="ILocalStorageUrlSigner"/> signature (minted only by the storage service after a normal
/// authenticated handler authorised the download) AND the owning module's
/// <see cref="ILocalStorageObjectResolver"/> must re-confirm the key still belongs to a live, Clean
/// record. A bucket with no registered resolver is never served (fail closed).
/// </summary>
public static class LocalStorageBuckets
{
    public const string ProfilePhotos = "profile-photos";
    public const string Documents = "documents";
    public const string CandidateDocuments = "candidate-documents";
    public const string SupportAttachments = "support-attachments";

    public const string RoutePrefix = "/api/dev/local-storage";

    private static readonly Dictionary<string, string> Roots = new(StringComparer.Ordinal)
    {
        [ProfilePhotos]      = Path.Combine("onebigteam", "profile-photos"),
        [Documents]          = Path.Combine("onebigteam", "documents"),
        [CandidateDocuments] = Path.Combine("onebigteam", "recruitment", "candidate-documents"),
        [SupportAttachments] = Path.Combine("onebigteam", "support-attachments"),
    };

    /// <summary>Every known bucket name (exact, lower-case — bucket matching is ordinal).</summary>
    public static IReadOnlyCollection<string> All => Roots.Keys;

    /// <summary>
    /// Resolves the absolute, canonical root directory for <paramref name="bucket"/>. Matching is
    /// ordinal (case-sensitive) so a differently-cased bucket name is rejected rather than aliased.
    /// </summary>
    public static bool TryGetRootPath(string bucket, out string rootPath)
    {
        if (bucket is not null && Roots.TryGetValue(bucket, out var relative))
        {
            rootPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), relative));
            return true;
        }

        rootPath = string.Empty;
        return false;
    }

    public static string GetRootPath(string bucket) =>
        TryGetRootPath(bucket, out var root)
            ? root
            : throw new ArgumentOutOfRangeException(nameof(bucket), bucket, "Unknown local storage bucket.");
}
