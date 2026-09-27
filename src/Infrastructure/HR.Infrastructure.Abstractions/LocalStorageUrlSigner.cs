using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HR.Infrastructure.Abstractions;

/// <summary>
/// HMAC-SHA256 implementation of <see cref="ILocalStorageUrlSigner"/> for the Development/test
/// local-storage fallback only (it is registered exclusively in the same branches that register a
/// <c>Local*StorageService</c>; production Supabase storage never touches it).
///
/// The signing key is 256 random bits generated per process and never persisted or configured, so it
/// cannot leak through config/source control and every URL dies with the process that minted it.
/// The signed message binds a version tag, the bucket, the exact storage key and the expiry, so a
/// signature for one file can never be replayed against another file, another bucket, or a later
/// expiry.
///
/// URLs are deliberately multi-use within their short lifetime — the same semantics as the Supabase
/// signed URLs used in production (browsers can legitimately fetch an &lt;img&gt; source or a
/// redirected download more than once). The lifetime is far shorter than production's, and every
/// fetch is still re-checked against the database by the bucket's <see cref="ILocalStorageObjectResolver"/>.
/// </summary>
public sealed class LocalStorageUrlSigner : ILocalStorageUrlSigner
{
    /// <summary>How long a minted URL stays valid.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    // Tolerance for an expiry slightly beyond now + Lifetime (clock granularity); anything further out
    // cannot have been minted by this signer and is rejected even if the HMAC somehow matched.
    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromSeconds(30);

    private const int KeySizeBytes = 32;
    private const int SignatureSizeBytes = 32; // HMAC-SHA256

    private readonly byte[] _key;
    private readonly TimeProvider _timeProvider;

    public LocalStorageUrlSigner(TimeProvider timeProvider)
        : this(RandomNumberGenerator.GetBytes(KeySizeBytes), timeProvider)
    {
    }

    public LocalStorageUrlSigner(byte[] key, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (key.Length < KeySizeBytes)
            throw new ArgumentException($"The signing key must be at least {KeySizeBytes} bytes.", nameof(key));

        _key = key.ToArray();
        _timeProvider = timeProvider;
    }

    public Uri CreateSignedUrl(string baseUrl, string bucket, string storageKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        if (!LocalStorageBuckets.TryGetRootPath(bucket, out _))
            throw new ArgumentOutOfRangeException(nameof(bucket), bucket, "Unknown local storage bucket.");

        var expires = _timeProvider.GetUtcNow().Add(Lifetime).ToUnixTimeSeconds()
            .ToString(CultureInfo.InvariantCulture);
        var signature = Base64Url.EncodeToString(ComputeSignature(bucket, storageKey, expires));

        var encodedKey = string.Join('/', storageKey.Split('/').Select(Uri.EscapeDataString));
        return new Uri(
            $"{baseUrl.TrimEnd('/')}{LocalStorageBuckets.RoutePrefix}/{bucket}/{encodedKey}"
            + $"?exp={expires}&sig={signature}");
    }

    public bool IsValid(string bucket, string storageKey, string? expires, string? signature)
    {
        if (string.IsNullOrEmpty(bucket) || string.IsNullOrEmpty(storageKey)
            || string.IsNullOrEmpty(expires) || string.IsNullOrEmpty(signature))
            return false;

        if (!long.TryParse(expires, NumberStyles.None, CultureInfo.InvariantCulture, out var expiresUnix))
            return false;

        var now = _timeProvider.GetUtcNow();
        DateTimeOffset expiresAt;
        try
        {
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresUnix);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        if (expiresAt <= now || expiresAt > now + Lifetime + MaxClockSkew)
            return false;

        byte[] provided;
        try
        {
            provided = Base64Url.DecodeFromChars(signature);
        }
        catch (FormatException)
        {
            return false;
        }

        if (provided.Length != SignatureSizeBytes)
            return false;

        var expected = ComputeSignature(bucket, storageKey, expires);
        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    private byte[] ComputeSignature(string bucket, string storageKey, string expires)
    {
        var message = Encoding.UTF8.GetBytes($"obt-local-storage-v1\n{bucket}\n{storageKey}\n{expires}");
        return HMACSHA256.HashData(_key, message);
    }
}
