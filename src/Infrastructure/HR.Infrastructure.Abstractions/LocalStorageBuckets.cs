namespace HR.Infrastructure.Abstractions;

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
