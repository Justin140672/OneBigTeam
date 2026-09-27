namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Mints and validates the short-lived, HMAC-signed URLs handed out by the Development/test
/// <c>Local*StorageService</c> fallbacks. A storage service only calls
/// <see cref="CreateSignedUrl"/> from <c>GetDownloadUrlAsync</c>, which in turn is only reached after
/// the normal authenticated handler has authorised the caller and checked the file is Clean — the
/// local equivalent of a Supabase signed URL.
/// </summary>
public interface ILocalStorageUrlSigner
{
    /// <summary>
    /// Builds <c>{baseUrl}/api/dev/local-storage/{bucket}/{key}?exp=…&amp;sig=…</c>, where the
    /// signature covers the bucket, the exact storage key and the expiry.
    /// </summary>
    Uri CreateSignedUrl(string baseUrl, string bucket, string storageKey);

    /// <summary>
    /// True only when <paramref name="signature"/> is a valid signature for exactly this bucket, key
    /// and expiry, and the expiry is in the future (and not implausibly far in the future).
    /// </summary>
    bool IsValid(string bucket, string storageKey, string? expires, string? signature);
}
